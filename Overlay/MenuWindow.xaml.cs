using System.Linq;
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

    // Tamaño de los círculos del menú, configurable (Complete) desde Configuración.
    // Gap y ColGap escalan junto con el ítem para mantener proporciones.
    private double Item   => SettingsService.Current.MenuItemSize;
    private double Gap    => Item * 0.25;
    private double ColGap => Item * 0.25;

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

        // Dirección horizontal: si el botón está en la mitad izquierda de Edge, el menú
        // (gear y columnas de overflow) se despliega hacia la DERECHA para no salirse.
        _rightward = SettingsService.Current.ButtonRelX < 0.5;
        double hstep = (Item / 2 + Gap + Item / 2);

        // Botón + (siempre arriba del FAB)
        var addBtn = MakeCircle("＋", null,
            () => _manager.OpenAddAppDialog(),
            (Brush)FindResource("Md3Primary"),
            (Brush)FindResource("Md3OnPrimary"));
        addBtn.Tag = "fixed:add";
        double addCx = bx, addCy = by - (Item / 2 + Gap + Item / 2);
        Place(addBtn, addCx, addCy);
        AnimateEntry(addBtn, "fixed:add", (addCx - Item / 2, addCy - Item / 2), 0);

        // Botón ⚙ (al lado del FAB, en la dirección de despliegue)
        var gearBtn = MakeCircle("⚙", null,
            () => _manager.OpenSettings(),
            (Brush)FindResource("Md3SurfaceContainerHigh"),
            (Brush)FindResource("Md3OnSurface"));
        gearBtn.Tag = "fixed:gear";
        double gearCx = bx + (_rightward ? hstep : -hstep), gearCy = by;
        Place(gearBtn, gearCx, gearCy);
        AnimateEntry(gearBtn, "fixed:gear", (gearCx - Item / 2, gearCy - Item / 2), 1);

        var apps = SettingsService.Current.Apps;
        if (apps.Count == 0) return;

        BuildAppItems(bx, by, startYFor: upward, edgeTop, edgeBottom, edgeHeight);
    }

    // ── Carpetas / grupos ──

    // El estado de carpeta expandida vive en el OverlayManager (persiste entre
    // aperturas del menú mientras Edge esté abierto). Solo una carpeta a la vez.

    private const double ChildScale = 0.85; // apps dentro de carpeta: 85% del tamaño normal

    // FLIP (First-Last-Invert-Play): al expandir/colapsar una carpeta reconstruimos
    // el árbol igual que siempre, pero los elementos que YA existían arrancan
    // visualmente en su posición vieja y se deslizan a la nueva (en vez de saltar).
    // _prevPositions guarda el (left, top) por Key antes de limpiar; _slideExisting
    // activa el deslizamiento en el próximo BuildLayout.
    private readonly Dictionary<string, (double left, double top)> _prevPositions = new();
    private bool _slideExisting;

    // Dirección horizontal del despliegue (columnas de overflow y gear).
    private bool _rightward;

    /// <summary>Unidad visual del menú: una app suelta, una carpeta, o una app hija de carpeta.</summary>
    private sealed class MenuUnit
    {
        public required FrameworkElement Element;
        public required double Size;
        public string? GroupId;       // no nulo si es hija de carpeta (para la pill de fondo)
        public bool IsFolderHeader;   // true si es el círculo de la carpeta
        public string? Key;           // clave estable para diff de posición (FLIP): app:Id o group:Id
    }

    private void BuildAppItems(double bx, double by, bool startYFor, double edgeTop, double edgeBottom, double edgeHeight)
    {
        bool upward = startYFor;
        var s = SettingsService.Current;
        bool groupsOn = s.Groups.Count > 0;

        // Construir la secuencia lógica de unidades (orden = orden de Administrar apps,
        // con las carpetas insertadas en la posición de su primera app).
        var units = new List<MenuUnit>();

        if (!groupsOn)
        {
            foreach (var app in s.Apps)
                units.Add(new MenuUnit { Element = MakeAppCircle(app, 0.5, Item), Size = Item, Key = "app:" + app.Id });
        }
        else
        {
            var seenGroups = new HashSet<string>();
            foreach (var app in s.Apps)
            {
                if (string.IsNullOrEmpty(app.GroupId) || s.Groups.All(g => g.Id != app.GroupId))
                {
                    units.Add(new MenuUnit { Element = MakeAppCircle(app, 0.5, Item), Size = Item, Key = "app:" + app.Id });
                    continue;
                }

                // App agrupada: en la posición de la PRIMERA app del grupo, insertar la carpeta.
                if (seenGroups.Add(app.GroupId))
                {
                    var group = s.Groups.First(g => g.Id == app.GroupId);
                    units.Add(new MenuUnit
                    {
                        Element = MakeFolderCircle(group),
                        Size = Item,
                        IsFolderHeader = true,
                        Key = "group:" + group.Id
                    });

                    // Si está expandida, agregar sus apps (más chicas) justo después.
                    if (_manager.ExpandedGroupId == group.Id)
                    {
                        double cs = Item * ChildScale;
                        foreach (var child in s.Apps.Where(a => a.GroupId == group.Id))
                            units.Add(new MenuUnit
                            {
                                Element = MakeAppCircle(child, 0.5, cs),
                                Size = cs,
                                GroupId = group.Id,
                                Key = "app:" + child.Id
                            });
                    }
                }
            }
        }

        if (upward) units.Reverse();

        // Colocar las unidades en columna desde el botón hacia afuera, con overflow.
        double colX   = bx;
        double cursor = upward
            ? by - (Item / 2 + Gap + Item + Gap)   // arranca arriba del + 
            : by + (Item / 2 + Gap + Item / 2);
        double margin = 16;
        int delay = 2;

        // Para dibujar las pills de fondo: acumular rangos (x, yTop, yBottom) por grupo.
        var pillRuns = new Dictionary<string, (double x, double top, double bottom)>();

        foreach (var u in units)
        {
            double half = u.Size / 2;

            // Posición del centro según dirección. En 'upward' cursor marca el borde inferior.
            double centerY = upward ? cursor - half : cursor + half;

            bool overflow = upward
                ? (centerY - half < edgeTop + margin)
                : (centerY + half > edgeBottom - margin);
            if (overflow)
            {
                colX  += _rightward ? (Item + ColGap) : -(Item + ColGap);
                cursor = upward
                    ? by - (Item / 2 + Gap + Item + Gap)
                    : by + (Item / 2 + Gap + Item / 2);
                centerY = upward ? cursor - half : cursor + half;
            }

            // Registrar rango de pill para apps hijas de carpeta.
            if (u.GroupId != null)
            {
                if (pillRuns.TryGetValue(u.GroupId, out var r))
                    pillRuns[u.GroupId] = (colX, Math.Min(r.top, centerY - half), Math.Max(r.bottom, centerY + half));
                else
                    pillRuns[u.GroupId] = (colX, centerY - half, centerY + half);
            }

            double finalLeft = colX - u.Size / 2;
            double finalTop  = centerY - u.Size / 2;
            u.Element.Tag = u.Key; // para capturar su posición en el próximo rebuild (FLIP)
            PlaceSized(u.Element, colX, centerY, u.Size);
            AnimateEntry(u.Element, u.Key, (finalLeft, finalTop), delay);
            delay++;

            cursor += upward ? -(u.Size + Gap) : (u.Size + Gap);
        }

        // Dibujar las pills de fondo DETRÁS de los íconos hijos.
        foreach (var (x, top, bottom) in pillRuns.Values)
            DrawGroupPill(x, top, bottom);
    }

    /// <summary>Fondo tipo "pill" (extremos semicirculares) con el color primario translúcido.</summary>
    private void DrawGroupPill(double centerX, double top, double bottom)
    {
        double pad = Item * 0.12;
        double w = Item * ChildScale + pad * 2;
        double h = (bottom - top) + pad * 2;

        var primary = ((SolidColorBrush)FindResource("Md3Primary")).Color;
        var fill = new SolidColorBrush(Color.FromArgb(0x33, primary.R, primary.G, primary.B));

        var pill = new Border
        {
            Width        = w,
            Height       = h,
            CornerRadius = new CornerRadius(w / 2), // extremos redondos = pill
            Background   = fill,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(pill, centerX - w / 2);
        Canvas.SetTop(pill, top - pad);
        Root.Children.Insert(0, pill); // detrás de todo
    }

    private Grid MakeFolderCircle(AppGroup group)
    {
        var grid = MakeCircle("📁", null,
            () => ToggleFolder(group.Id),
            (Brush)FindResource("Md3SurfaceContainerHigh"),
            (Brush)FindResource("Md3OnSurface"));

        grid.ToolTip = group.Name;

        // Badge de cantidad de apps en la carpeta.
        int count = SettingsService.Current.Apps.Count(a => a.GroupId == group.Id);
        if (count > 0)
        {
            double bs = Item * 0.42;
            grid.Children.Add(new Border
            {
                Width = bs, Height = bs,
                CornerRadius = new CornerRadius(bs / 2),
                Background = (Brush)FindResource("Md3Primary"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Top,
                IsHitTestVisible    = false,
                Child = new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = bs * 0.5, FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("Md3OnPrimary"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            });
        }
        return grid;
    }

    private void ToggleFolder(string groupId)
    {
        // Una sola carpeta abierta a la vez: si ya estaba esta, se cierra; si no,
        // se abre esta (cerrando cualquier otra). El estado vive en el manager.
        bool opening = _manager.ExpandedGroupId != groupId;
        _manager.ExpandedGroupId = opening ? groupId : null;

        // FLIP: capturar la posición actual de cada elemento (por su Key) ANTES de
        // limpiar, para luego deslizarlos desde ahí a su lugar nuevo.
        _prevPositions.Clear();
        foreach (var child in Root.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is string key)
                _prevPositions[key] = (Canvas.GetLeft(child), Canvas.GetTop(child));
        }
        _slideExisting = true;

        // Recalcular el layout con el nuevo estado de expansión.
        Root.Children.Clear();
        var bx = _button.Left - Left + 32;
        var by = _button.Top  - Top  + 32;
        double edgeTopLocal, edgeBottomLocal;
        if (Win32.IsWindow(_manager.EdgeHwnd))
        {
            Win32.GetWindowRect(_manager.EdgeHwnd, out var er);
            double escale = Win32.DpiScaleOf(_manager.EdgeHwnd);
            edgeTopLocal    = er.Top    / escale - Top;
            edgeBottomLocal = er.Bottom / escale - Top;
        }
        else { edgeTopLocal = 0; edgeBottomLocal = Height; }
        BuildLayout(edgeTopLocal, edgeBottomLocal);

        // Restaurar el modo normal: la próxima apertura del menú anima todo.
        _slideExisting = false;
        _prevPositions.Clear();
    }

    private void PlaceSized(FrameworkElement el, double centerX, double centerY, double size)
    {
        Canvas.SetLeft(el, centerX - size / 2);
        Canvas.SetTop(el,  centerY - size / 2);
        Root.Children.Add(el);
    }

    // ── Helpers ──

    private Grid MakeCircle(string glyph, BitmapImage? img, Action onClick, Brush bg, Brush fg, double? sizeOverride = null)
    {
        double sz = sizeOverride ?? Item;
        var border = new Border
        {
            Width        = sz,
            Height       = sz,
            CornerRadius = new CornerRadius(sz / 2),
            Background   = bg,
            Cursor       = Cursors.Hand,
            Effect       = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.4, Color = Colors.Black }
        };

        if (img != null)
        {
            double imgSize = sz * 0.5;
            border.Child = new System.Windows.Controls.Image
            {
                Source              = img,
                Width               = imgSize,
                Height              = imgSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
        }
        else
        {
            border.Child = new TextBlock
            {
                Text                = glyph,
                FontSize            = sz * 0.46,
                Foreground          = fg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
        }

        var grid = new Grid
        {
            Width               = sz,
            Height              = sz,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        grid.RenderTransform = new ScaleTransform(0, 0);
        grid.Children.Add(border);
        border.MouseLeftButtonUp += (_, _) => onClick();
        return grid;
    }

    private Grid MakeAppCircle(AppEntry app, double originRelY, double size)
    {
        // Ícono desde caché: la primera vez descarga y guarda; luego es instantáneo.
        BitmapImage? img = IconCache.Get(IconCache.KeyFor(app));

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
            (Brush)FindResource("Md3OnSurface"),
            size);

        grid.MouseRightButtonUp += (_, _) =>
        {
            if (MessageBox.Show($"¿Quitar {app.Name}?", "QuickPanel",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _manager.RemoveApp(app);
        };

        grid.ToolTip = app.Name;

        bool showBadges = SettingsService.Current.ShowBadges;
        if (showBadges && _manager.Unread.TryGetValue(app.Id, out var n) && n > 0)
        {
            double bs = size * 0.42;
            var badge = new Border
            {
                Width  = bs,
                Height = bs,
                CornerRadius = new CornerRadius(bs / 2),
                Background   = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Top,
                IsHitTestVisible    = false,
                Child = new TextBlock
                {
                    Text       = n > 99 ? "99+" : n.ToString(),
                    FontSize   = bs * 0.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            };
            grid.Children.Add(badge);
        }

        return grid;
    }

    private void Place(FrameworkElement el, double centerX, double centerY)
    {
        Canvas.SetLeft(el, centerX - Item / 2);
        Canvas.SetTop(el,  centerY - Item / 2);
        Root.Children.Add(el);
    }

    /// <summary>Decide cómo entra un elemento al layout:
    /// - apertura normal del menú → scale-in escalonado.
    /// - expandir/colapsar carpeta → los que ya existían se DESLIZAN desde su posición
    ///   anterior (FLIP); los nuevos hacen scale-in.</summary>
    private void AnimateEntry(FrameworkElement el, string? key, (double left, double top)? finalPos, int index)
    {
        if (_slideExisting && key != null && finalPos is { } fp
            && _prevPositions.TryGetValue(key, out var prev))
        {
            ResetScale(el); // ya estaba a escala 1: no parpadear
            SlideFrom(el, prev.left - fp.left, prev.top - fp.top);
        }
        else
        {
            Animate(el, index);
        }
    }

    private void Animate(FrameworkElement el, int index)
    {
        var st   = ScaleOf(el);
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime      = TimeSpan.FromMilliseconds(index * 28),
            EasingFunction = ease
        };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    /// <summary>Deja el elemento en escala final (1) sin animación. Para elementos que
    /// ya estaban presentes cuando se expande una carpeta, así no parpadean.</summary>
    private static void ResetScale(FrameworkElement el)
    {
        var st = ScaleOf(el);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        st.ScaleX = 1;
        st.ScaleY = 1;
    }

    /// <summary>FLIP: el elemento ya está colocado en su posición FINAL; lo desplazamos
    /// visualmente al offset (dx, dy) de su posición vieja y animamos el offset a 0,
    /// de modo que se "desliza" del lugar anterior al nuevo.</summary>
    private static void SlideFrom(FrameworkElement el, double dx, double dy)
    {
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) return; // no se movió: nada que animar

        var tt = TranslateOf(el);
        tt.X = dx;
        tt.Y = dy;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur  = TimeSpan.FromMilliseconds(240);
        tt.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, dur) { EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, dur) { EasingFunction = ease });
    }

    /// <summary>Devuelve el ScaleTransform del elemento, envolviéndolo en un TransformGroup
    /// junto a un TranslateTransform si todavía no lo está (para poder combinar escala + traslación).</summary>
    private static ScaleTransform ScaleOf(FrameworkElement el)
    {
        if (el.RenderTransform is ScaleTransform st) return st;
        if (el.RenderTransform is TransformGroup g)
            return g.Children.OfType<ScaleTransform>().First();
        // Caso inesperado: rehacer con un ScaleTransform limpio.
        var ns = new ScaleTransform(1, 1);
        el.RenderTransform = ns;
        return ns;
    }

    /// <summary>Devuelve el TranslateTransform del elemento, promoviendo el RenderTransform
    /// a un TransformGroup (ScaleTransform + TranslateTransform) la primera vez.</summary>
    private static TranslateTransform TranslateOf(FrameworkElement el)
    {
        if (el.RenderTransform is TransformGroup g)
            return g.Children.OfType<TranslateTransform>().First();

        var scale = el.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        var trans = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(trans);
        el.RenderTransform = group;
        return trans;
    }

    private void Window_Deactivated(object sender, EventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
