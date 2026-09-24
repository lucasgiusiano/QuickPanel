using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using QuickPanel.Core;
using QuickPanel.Models;
using QuickPanel.Services;

namespace QuickPanel.Overlay;

/// <summary>
/// "Doble" liviano del panel para la animación de apertura.
///
/// El panel real no se puede animar: su WebView2 es una ventana nativa hija que ignora
/// los transforms de WPF (problema de "airspace"). Así que, igual que hace Windows con el
/// efecto de minimizar o con las miniaturas de Alt+Tab, se anima una IMAGEN del panel:
/// esta ventana transparente muestra la última captura de la app (o una tarjeta lisa si
/// todavía no hay ninguna) y crece desde el ícono que la abrió hasta el lugar del panel.
/// El panel real carga encubierto justo debajo y se descubre ANTES de que termine la
/// animación (<see cref="Play"/> avisa cuándo), así al llegar ya está pintado y usable.
///
/// Una instancia por overlay, reutilizada en cada apertura (crear una ventana
/// transparente por clic demoraba el arranque). No recibe foco ni clics.
/// </summary>
internal sealed class PanelRevealWindow : Window
{
    private const double Pad = 18;      // espacio para la sombra alrededor de la tarjeta
    private const double Corner = 8;    // mismo redondeo que aplica DWM a los paneles

    private readonly Border _card;
    private readonly Grid _inner = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _move = new();
    private Border? _iconLayer;         // Fancy: el ícono que "se transforma" en panel

    private PanelGeometry.Rect _target, _origin;
    private bool _fancy;

    public PanelRevealWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;

        // Sombra barata: dos contornos semitransparentes escalonados alrededor de la
        // tarjeta, en vez de DropShadowEffect (en una ventana transparente se calcula por
        // software en cada frame, sobre toda la tarjeta: era lo que más frenaba el arranque).
        Border ShadowRing(double spread, byte alpha) => new()
        {
            CornerRadius = new CornerRadius(Corner + spread),
            Margin = new Thickness(-spread, -spread + 2, -spread, -spread - 3),
            Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0))
        };

        _card = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(Corner),
            Child = _inner,
            RenderTransformOrigin = new Point(0, 0)
        };
        _card.SetResourceReference(Border.BackgroundProperty, "Md3Surface");

        var tg = new TransformGroup();
        tg.Children.Add(_scale);
        tg.Children.Add(_move);

        // La sombra acompaña a la tarjeta: ambas comparten el mismo transform.
        var cardHost = new Grid
        {
            Margin = new Thickness(Pad),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new Point(0, 0),
            RenderTransform = tg
        };
        cardHost.Children.Add(ShadowRing(9, 18));
        cardHost.Children.Add(ShadowRing(4, 34));
        cardHost.Children.Add(_card);
        Content = new Grid { Children = { cardHost } };
        _cardHost = cardHost;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex
                | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TRANSPARENT));
        };
    }

    private readonly Grid _cardHost;

    /// <summary>Crea el HWND de antemano (sin mostrar) para que la primera apertura no
    /// pague el costo de crear la ventana.</summary>
    public void Prewarm() => new WindowInteropHelper(this).EnsureHandle();

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>Arma el contenido y la posición para una apertura (con la ventana oculta).</summary>
    public void Prepare(AppEntry app, BitmapSource? preview,
                        PanelGeometry.Rect target, PanelGeometry.Rect origin, bool fancy)
    {
        _target = target;
        _origin = origin;
        _fancy = fancy;

        // Cortar cualquier animación/desvanecido anterior de esta misma instancia.
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        foreach (var dp in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            _scale.BeginAnimation(dp, null);
        _move.BeginAnimation(TranslateTransform.XProperty, null);
        _move.BeginAnimation(TranslateTransform.YProperty, null);
        _cardHost.BeginAnimation(OpacityProperty, null);

        Left = target.Left - Pad;
        Top = target.Top - Pad;
        Width = target.Width + Pad * 2;
        Height = target.Height + Pad * 2;
        _card.Width = target.Width;
        _card.Height = target.Height;

        var icon = IconCache.TryGetCached(IconCache.KeyFor(app));

        // Réplica visual del panel: barra de título + contenido.
        _inner.Children.Clear();
        _inner.RowDefinitions.Clear();
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Border { Background = Res("Md3SurfaceContainer") };
        var hs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 8, 0) };
        if (icon != null)
            hs.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0),
                                        VerticalAlignment = VerticalAlignment.Center });
        hs.Children.Add(new TextBlock
        {
            Text = app.Name, FontSize = 13, FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Md3OnSurface")
        });
        header.Child = hs;
        body.Children.Add(header);

        // Contenido: la última captura, o la superficie lisa del panel (sin pantalla de
        // carga: la app ya arranca su propia carga en el panel real).
        if (preview != null)
        {
            var img = new Image
            {
                Source = preview, Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.LowQuality);
            var host = new Border { Child = img, ClipToBounds = true };
            Grid.SetRow(host, 1);
            body.Children.Add(host);
        }
        _inner.Children.Add(body);

        _iconLayer = null;
        if (fancy)
        {
            // Capa del ícono: al principio la tarjeta ES el ícono, y se desvanece mientras
            // crece. Da la sensación de que el ícono se abre en el panel.
            _iconLayer = new Border { Background = Res("Md3SurfaceContainerHigh") };
            if (icon != null)
                _iconLayer.Child = new Image
                {
                    Source = icon, Width = 26, Height = 26,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            _inner.Children.Add(_iconLayer);
        }

        // Recorte redondeado del contenido (CornerRadius solo redondea el fondo).
        _inner.Clip = new RectangleGeometry(new Rect(0, 0, target.Width, target.Height), Corner, Corner);

        ApplyStartState();
    }

    // ── Animación ──

    private TimeSpan Duration => TimeSpan.FromMilliseconds(_fancy ? 420 : 180);

    /// <summary>Momento en que el panel real ya puede descubrirse debajo: la tarjeta lo
    /// tapa casi entero. Fancy: ~65% (con el easing ya supera el 90% del tamaño);
    /// Quick: cuando la tarjeta llega a opacidad completa.</summary>
    private TimeSpan UncoverAt => TimeSpan.FromMilliseconds(_fancy ? 270 : 120);

    private (double sx, double sy, double tx, double ty, double opacity) StartState()
    {
        if (_fancy)
        {
            // La tarjeta arranca con el tamaño y la posición exactos del ícono.
            double sx = Math.Max(0.02, _origin.Width / _target.Width);
            double sy = Math.Max(0.02, _origin.Height / _target.Height);
            return (sx, sy, _origin.Left - _target.Left, _origin.Top - _target.Top, 0.6);
        }

        // Quick: casi a tamaño final, encogida hacia el punto del panel más cercano al
        // ícono, así el movimiento igual "sale" del ícono sin recorrer la pantalla.
        const double s = 0.93;
        double px = Math.Clamp(_origin.CenterX, _target.Left, _target.Right) - _target.Left;
        double py = Math.Clamp(_origin.CenterY, _target.Top, _target.Bottom) - _target.Top;
        return (s, s, px * (1 - s), py * (1 - s), 0);
    }

    private void ApplyStartState()
    {
        var (sx, sy, tx, ty, op) = StartState();
        _scale.ScaleX = sx; _scale.ScaleY = sy;
        _move.X = tx; _move.Y = ty;
        _cardHost.Opacity = op;
    }

    /// <summary>
    /// Muestra la ventana y arranca la animación en su primer frame pintado (si arrancara
    /// antes, el comienzo se "perdería" y se vería como demora o salto).
    /// Devuelve: <c>firstFrame</c> (ya se ve), <c>uncover</c> (momento de descubrir el
    /// panel real debajo) y <c>done</c> (llegó).
    /// </summary>
    public (Task firstFrame, Task uncover, Task done) Play()
    {
        var first = new TaskCompletionSource<bool>();
        var uncover = new TaskCompletionSource<bool>();
        var done = new TaskCompletionSource<bool>();

        Show();

        EventHandler? onFrame = null;
        onFrame = (_, _) =>
        {
            CompositionTarget.Rendering -= onFrame;
            StartAnimations(done);
            first.TrySetResult(true);

            var t = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
                { Interval = UncoverAt };
            t.Tick += (_, _) => { t.Stop(); uncover.TrySetResult(true); };
            t.Start();
        };
        CompositionTarget.Rendering += onFrame;

        return (first.Task, uncover.Task, done.Task);
    }

    private void StartAnimations(TaskCompletionSource<bool> done)
    {
        var dur = Duration;
        IEasingFunction ease = _fancy
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.14 }   // leve rebote
            : new CubicEase { EasingMode = EasingMode.EaseOut };

        DoubleAnimation To(double v) => new(v, dur) { EasingFunction = ease };

        var last = To(0);
        last.Completed += (_, _) => done.TrySetResult(true);

        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, To(1));
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, To(1));
        _move.BeginAnimation(TranslateTransform.XProperty, To(0));
        _move.BeginAnimation(TranslateTransform.YProperty, last);
        _cardHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(_fancy ? 140 : 120)));

        _iconLayer?.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(230))
            { BeginTime = TimeSpan.FromMilliseconds(70) });
    }

    /// <summary>Se desvanece sobre el panel real (ya descubierto y usable debajo) y se oculta.</summary>
    public void FadeOut()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(_fancy ? 110 : 80));
        fade.Completed += (_, _) => { if (Opacity <= 0.01) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Ocultado inmediato (otra apertura interrumpió esta animación).</summary>
    public void HideNow()
    {
        BeginAnimation(OpacityProperty, null);
        Hide();
    }
}
