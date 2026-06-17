using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QuickPanel.Core;
using QuickPanel.Models;
using QuickPanel.Services;

// WinForms solo para Screen (sin UseWindowsForms global)
using WinScreen = System.Windows.Forms.Screen;
using WinPoint  = System.Drawing.Point;

namespace QuickPanel.Overlay;

public partial class MenuWindow : Window
{
    private readonly OverlayManager _manager;
    private readonly FloatingButtonWindow _button;

    private const double Item   = 48;
    private const double Gap    = 12;
    private const double ColGap = 12;

    public MenuWindow(OverlayManager manager, FloatingButtonWindow button)
    {
        _manager = manager;
        _button  = button;
        InitializeComponent();
        Loaded           += OnLoaded;
        SourceInitialized += (_, _) => MakeToolWindow();
    }

    private void MakeToolWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex   = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex | Win32.WS_EX_TOOLWINDOW));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // La ventana del menú cubre el monitor (para capturar clics y cerrarse),
        // pero el LAYOUT se calcula contra el rect de la ventana de Edge.
        var screen = WinScreen.FromPoint(
            new WinPoint((int)(_button.Left + 28), (int)(_button.Top + 28)));

        var wa    = screen.Bounds;
        var src   = PresentationSource.FromVisual(this)!;
        double sc = src.CompositionTarget.TransformToDevice.M11;

        Left   = wa.Left   / sc;
        Top    = wa.Top    / sc;
        Width  = wa.Width  / sc;
        Height = wa.Height / sc;

        // Rect de Edge en coords locales del canvas
        double edgeTopLocal, edgeBottomLocal;
        if (Win32.IsWindow(_manager.EdgeHwnd))
        {
            Win32.GetWindowRect(_manager.EdgeHwnd, out var er);
            double escale = Win32.DpiScaleOf(_manager.EdgeHwnd);
            edgeTopLocal    = er.Top    / escale - Top;
            edgeBottomLocal = er.Bottom / escale - Top;
        }
        else
        {
            edgeTopLocal = 0;
            edgeBottomLocal = Height;
        }

        BuildLayout(edgeTopLocal, edgeBottomLocal);
        Activate();
    }

    private void BuildLayout(double edgeTop, double edgeBottom)
    {
        Root.Children.Clear();

        double bx = _button.Left - Left + 32;
        double by = _button.Top  - Top  + 32;

        double edgeMid    = (edgeTop + edgeBottom) / 2.0;
        double edgeHeight = Math.Max(1, edgeBottom - edgeTop);
        bool upward = (by > edgeMid); // mitad inferior DE EDGE → desplegar hacia arriba

        // Botón + (siempre arriba del FAB)
        var addBtn = MakeCircle("＋", null,
            () => _manager.OpenAddAppDialog(),
            (Brush)FindResource("Md3Primary"),
            (Brush)FindResource("Md3OnPrimary"));
        Place(addBtn, bx, by - (Item / 2 + Gap + Item / 2));
        Animate(addBtn, 0);

        // Botón ⚙ (a la izquierda del FAB)
        var gearBtn = MakeCircle("⚙", null,
            () => _manager.OpenSettings(),
            (Brush)FindResource("Md3SurfaceContainerHigh"),
            (Brush)FindResource("Md3OnSurface"));
        Place(gearBtn, bx - (Item / 2 + Gap + Item / 2), by);
        Animate(gearBtn, 1);

        var apps = SettingsService.Current.Apps;
        if (apps.Count == 0) return;

        double startY = upward
            ? by - (Item / 2 + Gap + Item + Gap + Item / 2)
            : by + (Item / 2 + Gap + Item / 2);

        double colX   = bx;
        double curY   = startY;
        double margin = 16;
        int delay = 2;

        foreach (var app in apps)
        {
            // Overflow contra el rect de EDGE (no la pantalla)
            bool overflow = upward
                ? (curY - Item / 2 < edgeTop + margin)
                : (curY + Item / 2 > edgeBottom - margin);

            if (overflow)
            {
                colX -= (Item + ColGap);
                curY  = startY;
            }

            double originRelY = Math.Clamp((curY - edgeTop) / edgeHeight, 0.05, 0.95);
            var btn = MakeAppCircle(app, originRelY);
            Place(btn, colX, curY);
            Animate(btn, delay++);

            curY += upward ? -(Item + Gap) : (Item + Gap);
        }
    }

    // ── Helpers ──

    private Grid MakeCircle(string glyph, BitmapImage? img, Action onClick, Brush bg, Brush fg)
    {
        var border = new Border
        {
            Width        = Item,
            Height       = Item,
            CornerRadius = new CornerRadius(Item / 2),
            Background   = bg,
            Cursor       = Cursors.Hand,
            Effect       = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.4, Color = Colors.Black }
        };

        if (img != null)
        {
            border.Child = new System.Windows.Controls.Image
            {
                Source              = img,
                Width               = 24,
                Height              = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
        }
        else
        {
            border.Child = new TextBlock
            {
                Text                = glyph,
                FontSize            = 22,
                Foreground          = fg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
        }

        var grid = new Grid
        {
            Width               = Item,
            Height              = Item,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        grid.RenderTransform = new ScaleTransform(0, 0);
        grid.Children.Add(border);
        border.MouseLeftButtonUp += (_, _) => onClick();
        return grid;
    }

    private Grid MakeAppCircle(AppEntry app, double originRelY)
    {
        BitmapImage? img = null;
        try
        {
            // Ícono personalizado (Pro) si existe; si no, favicon desde la URL.
            var src = app.HasCustomIcon ? app.IconPath : AppEntry.FaviconFor(app.Url);
            if (!string.IsNullOrEmpty(src))
            {
                img = new BitmapImage();
                img.BeginInit();
                img.UriSource   = new Uri(src);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
            }
        }
        catch { img = null; }

        // Color de acento por app (Complete) si está definido.
        Brush bg = (Brush)FindResource("Md3SurfaceContainer");
        if (!string.IsNullOrEmpty(app.Color))
        {
            try { bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(app.Color)); }
            catch { /* color inválido: usar default */ }
        }

        var grid = MakeCircle(
            img == null ? (app.Name.Length > 0 ? app.Name[..1].ToUpper() : "?") : "",
            img,
            () => _manager.OpenApp(app, originRelY),
            bg,
            (Brush)FindResource("Md3OnSurface"));

        grid.MouseRightButtonUp += (_, _) =>
        {
            if (MessageBox.Show($"¿Quitar {app.Name}?", "QuickPanel",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _manager.RemoveApp(app);
        };

        grid.ToolTip = app.Name;
        return grid;
    }

    private void Place(FrameworkElement el, double centerX, double centerY)
    {
        Canvas.SetLeft(el, centerX - Item / 2);
        Canvas.SetTop(el,  centerY - Item / 2);
        Root.Children.Add(el);
    }

    private void Animate(FrameworkElement el, int index)
    {
        var st   = (ScaleTransform)el.RenderTransform;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime      = TimeSpan.FromMilliseconds(index * 28),
            EasingFunction = ease
        };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void Window_Deactivated(object sender, EventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
