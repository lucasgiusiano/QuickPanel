using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuickPanel.Core;
using QuickPanel.Models;
using QuickPanel.Services;

namespace QuickPanel.Overlay;

/// <summary>
/// Modo "Dock clásico": barra vertical auto-ocultable anclada al borde derecho de la
/// ventana del navegador. Colapsada muestra una pestaña "‹"; al acercar el cursor se
/// despliega deslizándose. Se mantiene abierta mientras haya un panel abierto.
/// Reemplaza por completo al botón flotante en este modo.
/// </summary>
public partial class DockBarWindow : Window
{
    private readonly OverlayManager _manager;
    private IntPtr _edgeOwner;

    private readonly DispatcherTimer _proximityTimer;
    private bool _expanded;
    private bool _animating;
    private int  _outsideTicks; // ticks consecutivos con el cursor fuera de la zona

    // Id de la carpeta expandida inline (una a la vez). El despliegue como cápsula
    // lateral con z-order sobre los paneles queda para la siguiente iteración.
    private string? _expandedGroupId;

    // True por un único RebuildApps: anima la entrada de las hijas al abrir la carpeta.
    private bool _animateChildrenOnce;

    private const double BarWidth = 64;
    private const double BarMarginRight = 14;   // separación de la barra respecto al borde del navegador
    private const double TopInset = 46;         // deja libre la franja de botones de la ventana (cerrar/min/max)
    private const double BottomInset = 14;
    private const double HotZoneInner = 16;     // cuánto entra (hacia la izquierda del borde) la franja que
                                                // dispara el despliegue. Pegada al borde, pero lo bastante ancha
                                                // para seguir siendo alcanzable bajo el ~8px de desborde que
                                                // Windows agrega a las ventanas maximizadas.

    public DockBarWindow(OverlayManager manager)
    {
        _manager = manager;
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            MakeToolWindow();
            ApplyEdgeOwner();
        };
        Loaded += (_, _) => RebuildApps();

        _proximityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _proximityTimer.Tick += (_, _) => UpdateProximity();
        _proximityTimer.Start();
        Closed += (_, _) => _proximityTimer.Stop();
    }

    // ── Anclaje de la barra (en DIPs de pantalla), para anclar paneles a su izquierda ──

    /// <summary>Rect del cuerpo de la barra en DIPs de pantalla (no de la ventana completa).
    /// Los paneles de app se anclan a la IZQUIERDA de este rect.</summary>
    public PanelGeometry.Rect BarRect()
    {
        double left = Left + (Width - BarMarginRight - BarWidth);
        return new PanelGeometry.Rect(left, Top + 16, BarWidth, Math.Max(1, Height - 32));
    }

    public void SetEdgeOwner(IntPtr edgeHwnd)
    {
        _edgeOwner = edgeHwnd;
        if (IsLoaded) ApplyEdgeOwner();
    }

    private void ApplyEdgeOwner()
    {
        if (_edgeOwner == IntPtr.Zero) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Win32.SetWindowLongPtr(hwnd, Win32.GWLP_HWNDPARENT, _edgeOwner);
    }

    private void MakeToolWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE,
            new IntPtr(ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE));
    }

    /// <summary>Reposiciona la ventana sobre el borde derecho del navegador, dejando libre
    /// la franja superior (botones de cerrar/min/max) y un margen inferior. Queda anclada
    /// abajo-derecha. La pestaña/barra se alinean a la derecha vía el layout XAML.</summary>
    public void Reanchor(IntPtr edgeHwnd)
    {
        if (!Win32.IsWindow(edgeHwnd) || Win32.IsIconic(edgeHwnd)) return;

        Win32.GetWindowRect(edgeHwnd, out var r);
        double scale = Win32.DpiScaleOf(edgeHwnd);

        double rightDip = r.Right / scale;
        double topDip = r.Top / scale;
        double hDip = r.Height / scale;

        Width = 220;
        Top = topDip + TopInset;
        Height = Math.Max(120, hDip - TopInset - BottomInset);
        // El borde derecho de la ventana coincide con el del navegador: la pestaña "‹"
        // (alineada a la derecha) queda pegada al borde. En ventanas maximizadas el
        // ~8px de desborde la recorta contra el borde de pantalla, lo que la deja a ras.
        Left = rightDip - Width;
    }

    // ── Despliegue / colapso ──
    // Nota: la decisión de expandir/colapsar la toma EXCLUSIVAMENTE el timer de
    // proximidad (UpdateProximity), que es puramente geométrico. No usamos MouseEnter
    // /MouseLeave sobre la barra: durante el deslizamiento, el cuerpo de la barra se
    // mueve por debajo del cursor quieto y dispara MouseEnter→MouseLeave espurios,
    // lo que provocaba un colapso+reexpansión (la animación corría dos veces).

    private void Tab_Click(object sender, MouseButtonEventArgs e) => Expand();

    /// <summary>Timer de proximidad: despliega al acercar el cursor al borde derecho;
    /// colapsa cuando el cursor se aleja y no hay panel abierto. Reusa el mismo enfoque
    /// que el auto-hide del botón flotante.</summary>
    private void UpdateProximity()
    {
        if (_animating) return;
        if (!Win32.GetCursorPos(out var p)) return;

        var src = PresentationSource.FromVisual(this);
        double scale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double cx = p.X / scale, cy = p.Y / scale;

        bool withinV = cy >= Top && cy <= Top + Height;
        double rightEdge = Left + Width;

        if (!_expanded)
        {
            // Franja caliente angosta pegada al borde derecho del navegador. Solo abarca
            // los últimos HotZoneInner px (más un pequeño margen externo), así que el
            // cursor solo dispara el despliegue al ir DECIDIDAMENTE al borde, y no al
            // tocar controles del contenido que están unos px hacia adentro.
            bool nearEdge = withinV && cx >= rightEdge - HotZoneInner && cx <= rightEdge + 4;
            if (nearEdge) Expand();
        }
        else
        {
            // Mantener abierta mientras el cursor esté entre el panel (a la izquierda
            // de la barra) y el borde derecho del navegador. La zona llega hasta
            // 'rightEdge + 8' —NO solo hasta el cuerpo de la barra— para cubrir el hueco
            // de separación (BarMarginRight): si terminara en el borde de la barra, al
            // desplegarse el cursor quedaba en ese hueco y se generaba un parpadeo
            // abrir/cerrar.
            var bar = BarRect();
            bool insideKeepZone =
                cx >= bar.Left - 12 && cx <= rightEdge + 20 &&
                cy >= Top - 8 && cy <= Top + Height + 8;

            // Histéresis: colapsar recién tras 2 ticks consecutivos afuera, para que un
            // único frame en el límite no dispare un colapso (y el consiguiente rebote).
            if (insideKeepZone || _manager.IsAnyPanelOpen)
            {
                _outsideTicks = 0;
            }
            else if (++_outsideTicks >= 2)
            {
                _outsideTicks = 0;
                Collapse();
            }
        }
    }

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        _animating = true;
        _outsideTicks = 0;

        Tab.Visibility = Visibility.Collapsed;
        Bar.Visibility = Visibility.Visible;

        // Desliza desde fuera de pantalla (a la derecha) hacia su lugar.
        SlideTransform.X = BarWidth + BarMarginRight;
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) => _animating = false;
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        _animating = true;

        var anim = new DoubleAnimation(BarWidth + BarMarginRight, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            _animating = false;
            if (!_expanded)
            {
                Bar.Visibility = Visibility.Collapsed;
                Tab.Visibility = Visibility.Visible;
                SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
                SlideTransform.X = 0;
            }
        };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>Cierra la barra si está abierta (ej. al abrir un panel desde un click).
    /// La regla de "permanecer abierta mientras haya panel" se evalúa en el timer.</summary>
    public void NotifyPanelStateChanged()
    {
        // Si se cerró el último panel y el cursor ya no está cerca, el timer colapsará solo.
    }

    // ── Construcción de la lista de apps (orden invertido vs menú Material) ──

    public void RebuildApps()
    {
        AppsList.Children.Clear();
        var s = SettingsService.Current;

        // Mismo orden que el menú Material (sin invertir).
        var apps = s.Apps.ToList();
        bool groupsOn = s.Groups.Count > 0;

        var seenGroups = new HashSet<string>();

        foreach (var app in apps)
        {
            bool grouped = groupsOn && !string.IsNullOrEmpty(app.GroupId)
                           && s.Groups.Any(g => g.Id == app.GroupId);

            if (!grouped)
            {
                AppsList.Children.Add(MakeAppButton(app, 44));
                continue;
            }

            // Carpeta: en la posición de su primera app, insertar el header. Si está
            // expandida, envolver carpeta + hijas en un "pill" con el color de la carpeta.
            if (seenGroups.Add(app.GroupId))
            {
                var group = s.Groups.First(g => g.Id == app.GroupId);

                if (_expandedGroupId == group.Id)
                {
                    var pill = new Border
                    {
                        CornerRadius        = new CornerRadius(26),
                        Background          = GroupTint(group, 46),   // color de la carpeta, tenue
                        Margin              = new Thickness(0, 3, 0, 3),
                        Padding             = new Thickness(0, 4, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    inner.Children.Add(MakeFolderButton(group));

                    int ci = 0;
                    foreach (var child in apps.Where(a => a.GroupId == group.Id))
                        inner.Children.Add(MakeAppButton(child, 36,
                            _animateChildrenOnce ? ci++ : (int?)null)); // hijas más chicas

                    pill.Child = inner;
                    AppsList.Children.Add(pill);
                }
                else
                {
                    AppsList.Children.Add(MakeFolderButton(group));
                }
            }
        }

        _animateChildrenOnce = false; // la animación es de un solo uso
    }

    private static Color GroupColorOf(AppGroup g)
    {
        var hex = string.IsNullOrEmpty(g.Color) ? "#64748B" : g.Color;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return (Color)ColorConverter.ConvertFromString("#64748B"); }
    }
    private static Brush GroupSolid(AppGroup g) => new SolidColorBrush(GroupColorOf(g));
    private static Brush GroupTint(AppGroup g, byte alpha)
    {
        var c = GroupColorOf(g); c.A = alpha; return new SolidColorBrush(c);
    }

    private FrameworkElement MakeFolderButton(AppGroup group)
    {
        var border = Circle(44, GroupSolid(group));   // el ícono lleva el color de la carpeta
        border.Child = new TextBlock
        {
            Text = "📁",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        border.ToolTip = group.Name;
        border.MouseLeftButtonUp += (_, _) =>
        {
            bool opening = _expandedGroupId != group.Id;
            _expandedGroupId = opening ? group.Id : null;
            _animateChildrenOnce = opening; // animar solo al abrir
            RebuildApps();
        };

        int count = SettingsService.Current.Apps.Count(a => a.GroupId == group.Id);
        if (count > 0) AddBadge(border, count.ToString(),
            (Brush)FindResource("Md3Primary"), (Brush)FindResource("Md3OnPrimary"));

        return Wrap(border);
    }

    private FrameworkElement MakeAppButton(AppEntry app, double size, int? animateIndex = null)
    {
        BitmapImage? img = IconCache.Get(IconCache.KeyFor(app));

        Brush bg = (Brush)FindResource("Md3SurfaceContainer");
        if (!string.IsNullOrEmpty(app.Color))
        {
            try { bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(app.Color)); }
            catch { /* color inválido: default */ }
        }

        var border = Circle(size, bg);
        if (img != null)
        {
            border.Child = new Image
            {
                Source = img,
                Width = size * 0.5,
                Height = size * 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            border.Child = new TextBlock
            {
                Text = app.Name.Length > 0 ? app.Name[..1].ToUpper() : "?",
                FontSize = size * 0.42,
                Foreground = (Brush)FindResource("Md3OnSurface"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        border.ToolTip = app.Name;
        border.MouseLeftButtonUp += (_, _) => _manager.OpenApp(app, 0.5);
        border.MouseRightButtonUp += (_, _) =>
        {
            if (MessageBox.Show(string.Format(Loc.T("Common_RemoveApp"), app.Name), "QuickPanel",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _manager.RemoveApp(app);
        };

        bool showBadges = SettingsService.Current.ShowBadges;
        if (showBadges && _manager.Unread.TryGetValue(app.Id, out var n) && n > 0)
            AddBadge(border, n > 99 ? "99+" : n.ToString(),
                new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), Brushes.White);

        if (animateIndex is { } idx) AnimateScaleIn(border, idx);

        return Wrap(border);
    }

    /// <summary>Scale-in escalonado (estilo Material) para la entrada de un ícono.</summary>
    private static void AnimateScaleIn(FrameworkElement el, int index)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(0, 0);
        el.RenderTransform = st;

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime = TimeSpan.FromMilliseconds(index * 35),
            EasingFunction = ease
        };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    // ── Helpers visuales ──

    private static Border Circle(double size, Brush bg) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = bg,
        Cursor = Cursors.Hand
    };

    /// <summary>Envuelve el círculo en un contenedor con margen vertical para el stack.</summary>
    private static FrameworkElement Wrap(Border b)
    {
        b.Margin = new Thickness(0, 3, 0, 3);
        b.HorizontalAlignment = HorizontalAlignment.Center;
        return b;
    }

    private static void AddBadge(Border host, string text, Brush bg, Brush fg)
    {
        double bs = host.Width * 0.42;
        var badge = new Border
        {
            Width = bs,
            Height = bs,
            CornerRadius = new CornerRadius(bs / 2),
            Background = bg,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text,
                FontSize = bs * 0.5,
                FontWeight = FontWeights.Bold,
                Foreground = fg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        // host es un círculo; para superponer el badge lo metemos en un Grid.
        if (host.Child is UIElement existing)
        {
            host.Child = null;
            var grid = new Grid();
            grid.Children.Add(existing);
            grid.Children.Add(badge);
            host.Child = grid;
        }
    }

    private void Add_Click(object sender, MouseButtonEventArgs e) => _manager.OpenAddAppDialog();
    private void Gear_Click(object sender, MouseButtonEventArgs e) => _manager.OpenSettings();
}
