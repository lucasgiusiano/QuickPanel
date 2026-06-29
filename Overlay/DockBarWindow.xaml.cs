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

    // Id de la carpeta expandida inline (una a la vez). El despliegue como cápsula
    // lateral con z-order sobre los paneles queda para la siguiente iteración.
    private string? _expandedGroupId;

    private const double BarWidth = 64;
    private const double BarMarginRight = 8;

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
        var ex   = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE,
            new IntPtr(ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE));
    }

    /// <summary>Reposiciona la ventana sobre el borde derecho del navegador, cubriendo
    /// su alto. La pestaña/barra quedan ancladas a la derecha vía el layout XAML.</summary>
    public void Reanchor(IntPtr edgeHwnd)
    {
        if (!Win32.IsWindow(edgeHwnd) || Win32.IsIconic(edgeHwnd)) return;

        Win32.GetWindowRect(edgeHwnd, out var r);
        double scale = Win32.DpiScaleOf(edgeHwnd);

        double rightDip = r.Right / scale;
        double topDip   = r.Top   / scale;
        double hDip     = r.Height / scale;

        Width  = 220;
        Height = Math.Max(120, hDip);
        Left   = rightDip - Width;
        Top    = topDip;
    }

    // ── Despliegue / colapso ──

    private void Tab_MouseEnter(object sender, MouseEventArgs e) => Expand();
    private void Tab_Click(object sender, MouseButtonEventArgs e) => Expand();

    private void Bar_MouseLeave(object sender, MouseEventArgs e)
    {
        // Solo colapsar si no hay panel abierto; si lo hay, la barra permanece.
        if (!_manager.IsAnyPanelOpen) Collapse();
    }

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
            // Zona caliente: franja angosta pegada al borde derecho (la pestaña).
            bool nearTab = withinV && cx >= rightEdge - 40 && cx <= rightEdge + 4;
            if (nearTab) Expand();
        }
        else
        {
            // Colapsar si el cursor salió del cuerpo de la barra y no hay panel abierto.
            var bar = BarRect();
            bool insideBar =
                cx >= bar.Left - 12 && cx <= bar.Right + 8 &&
                cy >= Top && cy <= Top + Height;

            if (!insideBar && !_manager.IsAnyPanelOpen) Collapse();
        }
    }

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        _animating = true;

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

        // Orden invertido respecto al menú Material.
        var apps = s.Apps.AsEnumerable().Reverse().ToList();
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

            // Carpeta: en la posición de su primera app (en este orden), insertar el header.
            if (seenGroups.Add(app.GroupId))
            {
                var group = s.Groups.First(g => g.Id == app.GroupId);
                AppsList.Children.Add(MakeFolderButton(group));

                if (_expandedGroupId == group.Id)
                {
                    foreach (var child in apps.Where(a => a.GroupId == group.Id))
                        AppsList.Children.Add(MakeAppButton(child, 36)); // hijas más chicas
                }
            }
        }
    }

    private FrameworkElement MakeFolderButton(AppGroup group)
    {
        var border = Circle(44, (Brush)FindResource("Md3SurfaceContainerHigh"));
        border.Child = new TextBlock
        {
            Text = "📁", FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        border.ToolTip = group.Name;
        border.MouseLeftButtonUp += (_, _) =>
        {
            _expandedGroupId = _expandedGroupId == group.Id ? null : group.Id;
            RebuildApps();
        };

        int count = SettingsService.Current.Apps.Count(a => a.GroupId == group.Id);
        if (count > 0) AddBadge(border, count.ToString(),
            (Brush)FindResource("Md3Primary"), (Brush)FindResource("Md3OnPrimary"));

        return Wrap(border);
    }

    private FrameworkElement MakeAppButton(AppEntry app, double size)
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
                Source = img, Width = size * 0.5, Height = size * 0.5,
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
            if (MessageBox.Show($"¿Quitar {app.Name}?", "QuickPanel",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _manager.RemoveApp(app);
        };

        bool showBadges = SettingsService.Current.ShowBadges;
        if (showBadges && _manager.Unread.TryGetValue(app.Id, out var n) && n > 0)
            AddBadge(border, n > 99 ? "99+" : n.ToString(),
                new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), Brushes.White);

        return Wrap(border);
    }

    // ── Helpers visuales ──

    private static Border Circle(double size, Brush bg) => new()
    {
        Width = size, Height = size,
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
            Width = bs, Height = bs,
            CornerRadius = new CornerRadius(bs / 2),
            Background = bg,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text, FontSize = bs * 0.5, FontWeight = FontWeights.Bold,
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
