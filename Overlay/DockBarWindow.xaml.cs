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
/// Modo Dock: barra auto-ocultable anclada a un borde (derecho, izquierdo, inferior o,
/// solo en modo escritorio, superior) de su referencia: la ventana del navegador o el área
/// de trabajo del monitor. Colapsada muestra una pestaña; al acercar el cursor se despliega
/// deslizándose. Se mantiene abierta mientras haya un panel abierto. En los bordes
/// superior/inferior la barra es horizontal. La pestaña se puede mover a lo largo de su
/// borde desde el "modo mover" (Configuración o atajo), igual que el botón flotante.
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

    // Reordenar íconos arrastrándolos (clic vs arrastre por umbral de movimiento).
    private readonly IconDragReorder _drag;

    // El navegador está en pantalla completa (video/F11): lo informa el OverlayManager.
    private bool _fullscreen;

    private const double BaseBarThick = 64;     // grosor de la barra a tamaño Normal (ancho si es vertical, alto si horizontal)
    private const double BarMarginEdge = 14;    // separación de la barra respecto al borde de la referencia
    private const double BarMarginAlong = 16;   // margen en los extremos de la barra
    private const double WinThick = 220;        // profundidad de la ventana (más que la barra: pestaña + cápsula)
    private const double BrowserCaptionInset = 46; // navegador, bordes laterales: franja de botones cerrar/min/max
    private const double BrowserInset = 14;     // navegador: resto de extremos
    private const double DesktopInset = 8;      // escritorio: sin chrome que esquivar
    private const double BaseTabLong = 64, BaseTabShort = 18;

    /// <summary>
    /// Escala del dock (Configuración → Dock → Tamaño): 1 = Normal, ~0.84 = Medium,
    /// ~0.69 = Slim. Se lee en vivo, así cambiarla se aplica sin recrear el dock.
    /// </summary>
    private static double Scale => Math.Clamp(SettingsService.Current.DockScale, 0.5, 1.0);

    private static double BarThick => BaseBarThick * Scale;
    private static double TabLong  => BaseTabLong * Scale;
    // La pestaña se achica menos que el resto: por debajo de ~14px cuesta acertarle.
    private static double TabShort => Math.Max(14, BaseTabShort * Scale);
    private const double HotZoneInner = 16;     // cuánto entra (desde el borde hacia adentro) la franja que
                                                // dispara el despliegue. Pegada al borde, pero lo bastante ancha
                                                // para seguir siendo alcanzable bajo el ~8px de desborde que
                                                // Windows agrega a las ventanas maximizadas.

    // Borde fijo durante la vida de esta ventana: cambiarlo en Configuración recrea los overlays.
    private readonly DockEdge _edge;

    /// <summary>True si la barra es horizontal (dock arriba o abajo).</summary>
    public bool IsHorizontal => _edge is DockEdge.Top or DockEdge.Bottom;

    // Modo mover la pestaña a lo largo del borde.
    private bool _moveMode;
    private bool _tabDragging;
    private double _moveFrac;

    public DockBarWindow(OverlayManager manager)
    {
        _manager = manager;
        _edge = manager.Anchor.DockEdge;
        InitializeComponent();
        ApplyEdgeLayout();

        _drag = new IconDragReorder(this, DraggableIcons, (src, dst) =>
        {
            if (!AppOrdering.ApplyDrop(src, dst)) return;
            AppOrdering.Commit();
            App.RefreshAppLists(); // este dock y los de las demás ventanas
        });

        SourceInitialized += (_, _) =>
        {
            MakeToolWindow();
            ApplyEdgeOwner();
        };
        Loaded += (_, _) =>
        {
            RebuildApps();
            ApplyTabVisibility(); // sin esperar al primer tick: evita un parpadeo de la pestaña
        };

        _proximityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _proximityTimer.Tick += (_, _) => UpdateProximity();
        _proximityTimer.Start();

        // Cuando un panel captura el favicon real de su página, el ícono que el dock ya
        // dibujó (el aproximado remoto) queda viejo: redibujar. Llega desde otro hilo.
        IconCache.IconUpdated += OnIconUpdated;

        Closed += (_, _) =>
        {
            _proximityTimer.Stop();
            // Imprescindible: el evento es estático y hay un dock por ventana de navegador,
            // que además se recrean en RebuildOverlays. Sin esto quedan vivos para siempre.
            IconCache.IconUpdated -= OnIconUpdated;
        };
    }

    private void OnIconUpdated(string key) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded) return;
            if (SettingsService.Current.Apps.Any(a => IconCache.KeyFor(a) == key))
                RebuildApps();
        });

    // ── Anclaje de la barra (en DIPs de pantalla), para anclar paneles a su izquierda ──

    /// <summary>Rect del cuerpo de la barra en DIPs de pantalla (no de la ventana completa).
    /// Los paneles de app se anclan a su costado (vertical) o encima/debajo (horizontal).</summary>
    public PanelGeometry.Rect BarRect()
    {
        double along = Math.Max(1, (IsHorizontal ? Width : Height) - 2 * BarMarginAlong);
        return _edge switch
        {
            DockEdge.Left   => new(Left + BarMarginEdge, Top + BarMarginAlong, BarThick, along),
            DockEdge.Bottom => new(Left + BarMarginAlong, Top + Height - BarMarginEdge - BarThick, along, BarThick),
            DockEdge.Top    => new(Left + BarMarginAlong, Top + BarMarginEdge, along, BarThick),
            _               => new(Left + Width - BarMarginEdge - BarThick, Top + BarMarginAlong, BarThick, along)
        };
    }

    public void SetEdgeOwner(IntPtr edgeHwnd)
    {
        _edgeOwner = edgeHwnd;
        // Modo escritorio (sin navegador dueño): siempre visible sobre las demás ventanas.
        Topmost = edgeHwnd == IntPtr.Zero;
        if (IsLoaded) ApplyEdgeOwner();
    }

    // ── Layout según el borde ──

    /// <summary>
    /// Aplica alineaciones, márgenes, tamaños y orientación de la pestaña, la barra y la
    /// lista de apps para el borde elegido. El XAML trae los valores del borde derecho.
    /// </summary>
    private void ApplyEdgeLayout()
    {
        bool h = IsHorizontal;
        double overflow = _manager.Anchor.EdgeOverflow;

        // Pestaña: pegada al borde (con el margen del desborde invisible de DWM en el
        // navegador); su posición a lo largo del borde la pone ApplyTabPosition.
        Tab.Width  = h ? TabLong : TabShort;
        Tab.Height = h ? TabShort : TabLong;
        TabGlyph.FontSize = 20 * Math.Max(0.8, Scale);

        // Tamaño: todo el contenido de la barra (logo, "+", apps, carpetas, badges, ⚙)
        // se escala junto con un LayoutTransform, así las proporciones quedan idénticas a
        // las del tamaño Normal en los 4 bordes. El grosor de la barra acompaña.
        double k = Scale;
        BarDock.LayoutTransform = k >= 0.999 ? Transform.Identity : new ScaleTransform(k, k);
        Bar.CornerRadius = new CornerRadius(32 * k);
        switch (_edge)
        {
            case DockEdge.Left:
                Tab.HorizontalAlignment = HorizontalAlignment.Left;
                Tab.VerticalAlignment   = VerticalAlignment.Top;
                Tab.CornerRadius = new CornerRadius(0, 8, 8, 0);
                TabRotate.Angle = 180;
                break;
            case DockEdge.Bottom:
                Tab.HorizontalAlignment = HorizontalAlignment.Left;
                Tab.VerticalAlignment   = VerticalAlignment.Bottom;
                Tab.CornerRadius = new CornerRadius(8, 8, 0, 0);
                TabRotate.Angle = 90;
                break;
            case DockEdge.Top:
                Tab.HorizontalAlignment = HorizontalAlignment.Left;
                Tab.VerticalAlignment   = VerticalAlignment.Top;
                Tab.CornerRadius = new CornerRadius(0, 0, 8, 8);
                TabRotate.Angle = -90;
                break;
            default:
                Tab.HorizontalAlignment = HorizontalAlignment.Right;
                Tab.VerticalAlignment   = VerticalAlignment.Top;
                Tab.CornerRadius = new CornerRadius(8, 0, 0, 8);
                TabRotate.Angle = 0;
                break;
        }
        _tabOverflow = overflow;

        // Barra.
        if (h)
        {
            Bar.Width = double.NaN;
            Bar.Height = BarThick;
            Bar.HorizontalAlignment = HorizontalAlignment.Stretch;
            Bar.VerticalAlignment = _edge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            Bar.Margin = _edge == DockEdge.Top
                ? new Thickness(BarMarginAlong, BarMarginEdge, BarMarginAlong, 0)
                : new Thickness(BarMarginAlong, 0, BarMarginAlong, BarMarginEdge);
            BarDock.Margin = new Thickness(12, 0, 12, 0);
        }
        else
        {
            Bar.Width = BarThick;
            Bar.Height = double.NaN;
            Bar.VerticalAlignment = VerticalAlignment.Stretch;
            Bar.HorizontalAlignment = _edge == DockEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            Bar.Margin = _edge == DockEdge.Left
                ? new Thickness(BarMarginEdge, BarMarginAlong, 0, BarMarginAlong)
                : new Thickness(0, BarMarginAlong, BarMarginEdge, BarMarginAlong);
            BarDock.Margin = new Thickness(0, 12, 0, 12);
        }

        // Contenido de la barra: logo y "+" al inicio, ⚙ y separador al final, apps en el medio.
        var start = h ? Dock.Left : Dock.Top;
        var end   = h ? Dock.Right : Dock.Bottom;
        DockPanel.SetDock(LogoBtn, start);
        DockPanel.SetDock(AddBtn, start);
        DockPanel.SetDock(GearBtn, end);
        DockPanel.SetDock(Separator, end);
        LogoBtn.Margin = h ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 8);
        AddBtn.Margin  = h ? new Thickness(0, 0, 6, 0) : new Thickness(0, 0, 0, 6);
        GearBtn.Margin = h ? new Thickness(6, 0, 0, 0) : new Thickness(0, 6, 0, 0);
        foreach (var b in new FrameworkElement[] { LogoBtn, AddBtn, GearBtn })
        {
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.VerticalAlignment = VerticalAlignment.Center;
        }
        Separator.Width  = h ? 2 : 28;
        Separator.Height = h ? 28 : 2;
        Separator.Margin = h ? new Thickness(6, 0, 2, 0) : new Thickness(0, 6, 0, 2);
        Separator.VerticalAlignment = VerticalAlignment.Center;

        AppsScroll.VerticalScrollBarVisibility   = h ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        AppsScroll.HorizontalScrollBarVisibility = h ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        AppsList.Orientation = h ? Orientation.Horizontal : Orientation.Vertical;
        // Laterales: igual que siempre, apps de arriba hacia abajo.
        // Arriba/abajo: apps centradas a lo largo de la barra. Un ScrollViewer le da ancho
        // infinito a su contenido, así que centrar la lista adentro no alcanza: se centra el
        // propio ScrollViewer (toma el ancho de las apps y se centra en el hueco entre "+" y
        // ⚙). Si las apps no entran, ocupa todo el hueco y se desplaza con la rueda.
        AppsScroll.HorizontalAlignment = h ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        AppsList.HorizontalAlignment   = HorizontalAlignment.Center;
        AppsList.VerticalAlignment     = h ? VerticalAlignment.Center : VerticalAlignment.Top;
    }

    private double _tabOverflow;

    /// <summary>Coloca la pestaña a lo largo de su borde según la posición guardada (o la
    /// que se está arrastrando en modo mover). 0.5 = centrada, el comportamiento clásico.</summary>
    private void ApplyTabPosition()
    {
        double frac = _moveMode ? _moveFrac : _manager.Anchor.HandlePosition;
        bool h = IsHorizontal;
        double len = h ? ActualWidthOr(Width) : ActualHeightOr(Height);
        double off = Math.Max(0, frac * (len - TabLong));
        double o = _tabOverflow;
        var m = _edge switch
        {
            DockEdge.Left   => new Thickness(o, off, 0, 0),
            DockEdge.Bottom => new Thickness(off, 0, 0, o),
            DockEdge.Top    => new Thickness(off, o, 0, 0),
            _               => new Thickness(0, off, o, 0)
        };
        if (Tab.Margin != m) Tab.Margin = m;
    }

    private static double ActualWidthOr(double w) => double.IsNaN(w) ? 0 : w;
    private static double ActualHeightOr(double h) => double.IsNaN(h) ? 0 : h;

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

    /// <summary>
    /// Reposiciona la ventana sobre su borde de la referencia (ventana del navegador o área
    /// de trabajo del monitor). En el navegador, los bordes laterales dejan libre la franja
    /// superior de botones (cerrar/min/max); en escritorio no hay nada que esquivar.
    /// </summary>
    public void Reanchor()
    {
        var anchor = _manager.Anchor;
        if (!anchor.IsAlive || anchor.IsMinimized) return;

        var b = anchor.BoundsDip;
        bool desktop = anchor.IsDesktop;

        if (IsHorizontal)
        {
            double inset = desktop ? DesktopInset : BrowserInset;
            Width  = Math.Max(120, b.Width - 2 * inset);
            Height = WinThick;
            Left   = b.Left + inset;
            Top    = _edge == DockEdge.Top ? b.Top : b.Bottom - WinThick;
        }
        else
        {
            double startInset = desktop ? DesktopInset : BrowserCaptionInset;
            double endInset   = desktop ? DesktopInset : BrowserInset;
            Width  = WinThick;
            Height = Math.Max(120, b.Height - startInset - endInset);
            Top    = b.Top + startInset;
            // El borde exterior de la ventana coincide con el de la referencia: la pestaña
            // queda pegada al borde (en un navegador maximizado el ~8px de desborde la recorta
            // contra el borde de pantalla, lo que la deja a ras).
            Left   = _edge == DockEdge.Left ? b.Left : b.Right - WinThick;
        }
        ApplyTabPosition();
    }

    // ── Despliegue / colapso ──
    // Nota: la decisión de expandir/colapsar la toma EXCLUSIVAMENTE el timer de
    // proximidad (UpdateProximity), que es puramente geométrico. No usamos MouseEnter
    // /MouseLeave sobre la barra: durante el deslizamiento, el cuerpo de la barra se
    // mueve por debajo del cursor quieto y dispara MouseEnter→MouseLeave espurios,
    // lo que provocaba un colapso+reexpansión (la animación corría dos veces).

    // ── Pestaña: clic para desplegar, o arrastre en modo mover ──

    private void Tab_Down(object sender, MouseButtonEventArgs e)
    {
        if (!_moveMode) return;
        _tabDragging = true;
        Tab.CaptureMouse();
        e.Handled = true;
    }

    private void Tab_Move(object sender, MouseEventArgs e)
    {
        if (!_tabDragging) return;
        // Proyectado sobre UN eje: el del borde (vertical en laterales, horizontal arriba/abajo).
        var p = e.GetPosition(this);
        double along = IsHorizontal ? p.X : p.Y;
        double len = IsHorizontal ? Width : Height;
        _moveFrac = Math.Clamp((along - TabLong / 2) / Math.Max(1, len - TabLong), 0, 1);
        ApplyTabPosition();
    }

    private void Tab_Up(object sender, MouseButtonEventArgs e)
    {
        if (_moveMode)
        {
            if (_tabDragging) { _tabDragging = false; Tab.ReleaseMouseCapture(); }
            ExitMoveMode();
            e.Handled = true;
            return;
        }
        Expand();
    }

    /// <summary>
    /// Modo mover: la pestaña queda resaltada y visible (aunque esté configurada como
    /// oculta) y se arrastra a lo largo de su borde; soltar guarda la posición (por monitor
    /// en modo escritorio) y sale. Mientras dura, el despliegue por proximidad está apagado
    /// para que no compita con el arrastre.
    /// </summary>
    public void EnterMoveMode()
    {
        if (_expanded) Collapse();
        _moveMode = true;
        _moveFrac = _manager.Anchor.HandlePosition;
        Tab.SetResourceReference(Border.BackgroundProperty, "Md3Primary");
        TabGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Md3OnPrimary");
        Tab.Cursor = IsHorizontal ? Cursors.SizeWE : Cursors.SizeNS;
        ApplyTabVisibility();
        ApplyTabPosition();
    }

    private void ExitMoveMode()
    {
        _manager.Anchor.HandlePosition = _moveFrac;
        SettingsService.Save();
        _moveMode = false;
        Tab.SetResourceReference(Border.BackgroundProperty, "Md3PrimaryContainer");
        TabGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Md3OnPrimaryContainer");
        Tab.Cursor = Cursors.Hand;
        ApplyTabVisibility();
        ApplyTabPosition();
    }

    /// <summary>Informado por el OverlayManager en cada cambio de geometría del navegador.</summary>
    public void SetFullscreen(bool fullscreen)
    {
        if (_fullscreen == fullscreen) return;
        _fullscreen = fullscreen;
        ApplyTabVisibility();
    }

    /// <summary>
    /// La pestaña "‹" se oculta si el usuario lo eligió o si el navegador está en pantalla
    /// completa. "Ocultar pestaña" se ignora en modo "solo clic": sin pestaña no habría forma
    /// de abrir el dock (Configuración ya impide esa combinación; esto es la red de seguridad).
    /// El despliegue por proximidad no depende de la pestaña: sigue funcionando sin ella.
    /// </summary>
    private void ApplyTabVisibility()
    {
        if (_expanded || _animating) return; // durante/tras el despliegue la maneja Expand/Collapse
        var s = SettingsService.Current;
        bool hide = !_moveMode &&
                    ((s.HideDockHandle && !s.DockClickToOpen)
                     || (_fullscreen && s.HideInFullscreen));
        var v = hide ? Visibility.Collapsed : Visibility.Visible;
        if (Tab.Visibility != v) Tab.Visibility = v;
    }

    /// <summary>Timer de proximidad: despliega al acercar el cursor al borde derecho;
    /// colapsa cuando el cursor se aleja y no hay panel abierto. Reusa el mismo enfoque
    /// que el auto-hide del botón flotante.</summary>
    private void UpdateProximity()
    {
        if (_animating) return;
        if (_drag.IsDragging) return; // arrastrando un ícono: no colapsar bajo el cursor
        ApplyTabVisibility();          // aplica en caliente los cambios de Configuración
        ApplyTabPosition();            // y la posición de la pestaña (movida en otra ventana)
        if (_moveMode) return;         // modo mover: sin despliegue por proximidad
        if (!Win32.GetCursorPos(out var p)) return;

        var src = PresentationSource.FromVisual(this);
        double scale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var (along, depth, len) = EdgeLocal(p.X / scale, p.Y / scale);

        if (!_expanded)
        {
            // Franja caliente angosta pegada al borde. Solo abarca los últimos
            // HotZoneInner px (más un pequeño margen externo), así que el cursor solo
            // dispara el despliegue al ir DECIDIDAMENTE al borde, y no al tocar controles
            // del contenido que están unos px hacia adentro.
            // En modo "solo clic" el despliegue lo hace únicamente la pestaña (Tab_Up).
            if (SettingsService.Current.DockClickToOpen) return;
            bool nearEdge = along >= 0 && along <= len && depth <= HotZoneInner && depth >= -4;
            if (nearEdge) Expand();
        }
        else
        {
            // Mantener abierta mientras el cursor esté entre el borde y un poco más allá
            // de la barra. La zona se extiende también FUERA del borde (depth negativo) y
            // cubre el hueco de separación (BarMarginEdge): si terminara en el cuerpo de la
            // barra, al desplegarse el cursor quedaba en ese hueco y se generaba un
            // parpadeo abrir/cerrar.
            bool insideKeepZone =
                depth <= BarMarginEdge + BarThick + 12 && depth >= -(20 + OutsideGap()) &&
                along >= -8 && along <= len + 8;

            // Histéresis: colapsar recién tras 2 ticks consecutivos afuera, para que un
            // único frame en el límite no dispare un colapso (y el consiguiente rebote).
            if (insideKeepZone || _manager.IsAnyPanelOpen || AppContextMenu.IsOpen)
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

    /// <summary>
    /// Coordenadas del cursor relativas al borde del dock: <c>along</c> = posición a lo largo
    /// del borde (desde el inicio de la ventana), <c>depth</c> = distancia desde el borde
    /// exterior hacia adentro. Así la lógica de proximidad es la misma para los 4 bordes.
    /// </summary>
    private (double along, double depth, double len) EdgeLocal(double cx, double cy) => _edge switch
    {
        DockEdge.Left   => (cy - Top,  cx - Left,              Height),
        DockEdge.Bottom => (cx - Left, (Top + Height) - cy,    Width),
        DockEdge.Top    => (cx - Left, cy - Top,               Width),
        _               => (cy - Top,  (Left + Width) - cx,    Height)
    };

    /// <summary>
    /// Espacio (DIPs) entre el borde exterior del dock y el borde físico del monitor, del
    /// lado del dock. Típicamente la barra de tareas: con el dock abajo (o en el lado donde
    /// esté la barra de tareas) el cursor pasa por ella al ir hacia el borde, y sin contarla
    /// el dock se cerraba y volvía a abrir. Solo extiende la zona que MANTIENE abierto el
    /// dock, no la que lo abre: pasar por la barra de tareas no despliega nada.
    /// Tope de 120 para que una ventana de navegador no maximizada no deje una zona enorme.
    /// </summary>
    private double OutsideGap()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return 0;
            var mon = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
            var mi = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
            if (mon == IntPtr.Zero || !Win32.GetMonitorInfo(mon, ref mi)) return 0;

            double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var m = mi.rcMonitor;
            double gap = _edge switch
            {
                DockEdge.Left   => Left - m.Left / sc,
                DockEdge.Top    => Top - m.Top / sc,
                DockEdge.Bottom => m.Bottom / sc - (Top + Height),
                _               => m.Right / sc - (Left + Width)
            };
            return Math.Clamp(gap, 0, 120);
        }
        catch { return 0; }
    }

    /// <summary>Desplazamiento que deja la barra fuera de la vista (hacia su borde).</summary>
    private double HiddenOffset => (BarThick + BarMarginEdge) *
        (_edge is DockEdge.Left or DockEdge.Top ? -1 : 1);

    private DependencyProperty SlideProperty =>
        IsHorizontal ? TranslateTransform.YProperty : TranslateTransform.XProperty;

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        _animating = true;
        _outsideTicks = 0;

        Tab.Visibility = Visibility.Collapsed;
        Bar.Visibility = Visibility.Visible;

        // Desliza desde fuera de pantalla (del lado de su borde) hacia su lugar.
        var anim = new DoubleAnimation(HiddenOffset, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) => _animating = false;
        SlideTransform.BeginAnimation(SlideProperty, anim);
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        _animating = true;

        var prop = SlideProperty;
        var anim = new DoubleAnimation(HiddenOffset, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            _animating = false;
            if (!_expanded)
            {
                Bar.Visibility = Visibility.Collapsed;
                SlideTransform.BeginAnimation(prop, null);
                SlideTransform.X = 0;
                SlideTransform.Y = 0;
                ApplyTabVisibility();
            }
        };
        SlideTransform.BeginAnimation(prop, anim);
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
                    bool h = IsHorizontal;
                    var pill = new Border
                    {
                        CornerRadius        = new CornerRadius(26),
                        Background          = GroupTint(group, 46),   // color de la carpeta, tenue
                        Margin              = h ? new Thickness(3, 0, 3, 0) : new Thickness(0, 3, 0, 3),
                        Padding             = h ? new Thickness(4, 0, 4, 0) : new Thickness(0, 4, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center
                    };
                    var inner = new StackPanel
                    {
                        Orientation = h ? Orientation.Horizontal : Orientation.Vertical,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center
                    };
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
        _drag.Attach(border, "group:" + group.Id, () =>
        {
            bool opening = _expandedGroupId != group.Id;
            _expandedGroupId = opening ? group.Id : null;
            _animateChildrenOnce = opening; // animar solo al abrir
            RebuildApps();
        });

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
        // Clic = abrir; arrastrar = reordenar / meter o sacar de una carpeta.
        _drag.Attach(border, "app:" + app.Id, () => _manager.OpenApp(app, 0.5, IconRectDip(border)));
        border.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            AppContextMenu.Show(border, app, _manager);
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

    /// <summary>Íconos que aceptan un drop: apps y carpetas sueltas, más las hijas de la
    /// carpeta abierta (que viven dentro de la "pill").</summary>
    private IEnumerable<FrameworkElement> DraggableIcons()
    {
        foreach (var child in AppsList.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is string) yield return child;
            else if (child is Border { Child: Panel inner })
                foreach (var c in inner.Children.OfType<FrameworkElement>())
                    if (c.Tag is string) yield return c;
        }
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

    /// <summary>Envuelve el círculo con el margen de separación a lo largo de la barra.</summary>
    private FrameworkElement Wrap(Border b)
    {
        b.Margin = IsHorizontal ? new Thickness(3, 0, 3, 0) : new Thickness(0, 3, 0, 3);
        b.HorizontalAlignment = HorizontalAlignment.Center;
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    /// <summary>Rect del ícono en DIPs de pantalla (misma escala que Left/Top de esta
    /// ventana): en el dock horizontal el panel se alinea con el ícono que lo abrió.</summary>
    private PanelGeometry.Rect? IconRectDip(FrameworkElement el)
    {
        try
        {
            // Rect visual (incluye la escala del tamaño Medium/Slim), en DIPs de la ventana.
            var r = el.TransformToAncestor(this).TransformBounds(new Rect(el.RenderSize));
            return new PanelGeometry.Rect(Left + r.Left, Top + r.Top, r.Width, r.Height);
        }
        catch { return null; }
    }

    /// <summary>Reaplica el layout completo (tras cambiar el tamaño del dock).</summary>
    public void RefreshLayout()
    {
        ApplyEdgeLayout();
        RebuildApps();
        Reanchor();
    }

    /// <summary>Dock horizontal: la rueda del mouse desplaza la lista de apps a lo ancho.</summary>
    private void AppsScroll_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsHorizontal) return;
        AppsScroll.ScrollToHorizontalOffset(AppsScroll.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
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
