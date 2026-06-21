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
        Place(addBtn, bx, by - (Item / 2 + Gap + Item / 2));
        if (_animateOnlyGroupId == null) Animate(addBtn, 0); else ResetScale(addBtn);

        // Botón ⚙ (al lado del FAB, en la dirección de despliegue)
        var gearBtn = MakeCircle("⚙", null,
            () => _manager.OpenSettings(),
            (Brush)FindResource("Md3SurfaceContainerHigh"),
            (Brush)FindResource("Md3OnSurface"));
        Place(gearBtn, bx + (_rightward ? hstep : -hstep), by);
        if (_animateOnlyGroupId == null) Animate(gearBtn, 1); else ResetScale(gearBtn);

        var apps = SettingsService.Current.Apps;
        if (apps.Count == 0) return;

        BuildAppItems(bx, by, startYFor: upward, edgeTop, edgeBottom, edgeHeight);
    }

    // ── Carpetas / grupos ──

    // El estado de carpeta expandida vive en el OverlayManager (persiste entre
    // aperturas del menú mientras Edge esté abierto). Solo una carpeta a la vez.

    private const double ChildScale = 0.85; // apps dentro de carpeta: 85% del tamaño normal

    // Si no es null, solo se animan las apps hijas de este grupo (al expandir una
    // carpeta); el resto del menú se coloca sin animación para no parpadear todo.
    private string? _animateOnlyGroupId;

    // Dirección horizontal del despliegue (columnas de overflow y gear).
    private bool _rightward;

    /// <summary>Unidad visual del menú: una app suelta, una carpeta, o una app hija de carpeta.</summary>
    private sealed class MenuUnit
    {
        public required FrameworkElement Element;
        public required double Size;
        public string? GroupId;       // no nulo si es hija de carpeta (para la pill de fondo)
        public bool IsFolderHeader;   // true si es el círculo de la carpeta
    }

    private void BuildAppItems(double bx, double by, bool startYFor, double edgeTop, double edgeBottom, double edgeHeight)
    {
        bool upward = startYFor;
        var s = SettingsService.Current;
        bool groupsOn = LicenseService.HasFeature(Feature.Folders) && s.Groups.Count > 0;

        // Construir la secuencia lógica de unidades (orden = orden de Administrar apps,
        // con las carpetas insertadas en la posición de su primera app).
        var units = new List<MenuUnit>();

        if (!groupsOn)
        {
            foreach (var app in s.Apps)
                units.Add(new MenuUnit { Element = MakeAppCircle(app, 0.5, Item), Size = Item });
        }
        else
        {
            var seenGroups = new HashSet<string>();
            foreach (var app in s.Apps)
            {
                if (string.IsNullOrEmpty(app.GroupId) || s.Groups.All(g => g.Id != app.GroupId))
                {
                    units.Add(new MenuUnit { Element = MakeAppCircle(app, 0.5, Item), Size = Item });
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
                        IsFolderHeader = true
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
                                GroupId = group.Id
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

            PlaceSized(u.Element, colX, centerY, u.Size);
            // Si estamos en modo "animar solo una carpeta", solo las hijas de ese
            // grupo animan; el resto queda colocado en su lugar sin parpadear.
            if (_animateOnlyGroupId == null)
                Animate(u.Element, delay++);
            else if (u.GroupId == _animateOnlyGroupId)
                Animate(u.Element, delay++);
            else
                ResetScale(u.Element);

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

        // Al ABRIR, animar solo las apps reveladas de esta carpeta (el resto del menú
        // se reposiciona sin parpadear). Al cerrar, no hay nada nuevo: sin animación.
        _animateOnlyGroupId = opening ? groupId : "";

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
        _animateOnlyGroupId = null;
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

        bool showBadges = SettingsService.Current.ShowBadges
                          && LicenseService.HasFeature(Feature.Notifications);
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

    /// <summary>Deja el elemento en escala final (1) sin animación. Para elementos que
    /// ya estaban presentes cuando se expande una carpeta, así no parpadean.</summary>
    private static void ResetScale(FrameworkElement el)
    {
        var st = (ScaleTransform)el.RenderTransform;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        st.ScaleX = 1;
        st.ScaleY = 1;
    }

    private void Window_Deactivated(object sender, EventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
