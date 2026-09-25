using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuickPanel.Overlay;

/// <summary>
/// Distingue clic de arrastre sobre los íconos del Dock y del menú Material, y resuelve el
/// reordenamiento. Umbral de movimiento (no de tiempo): si el cursor se mueve más que
/// <see cref="SystemParameters.MinimumHorizontalDragDistance"/> con el botón apretado es un
/// arrastre; si se suelta antes, es un clic. Mismo criterio que usa Windows en el escritorio.
///
/// No usa DragDrop (OLE): su bucle modal no convive bien con ventanas WS_EX_NOACTIVATE ni
/// con el menú que se cierra al perder el foco. En su lugar captura el mouse y mueve el
/// ícono con un TranslateTransform. Cada ícono se identifica por su Tag ("app:Id"/"group:Id").
/// </summary>
internal sealed class IconDragReorder
{
    private readonly FrameworkElement _surface;                       // referencia de coordenadas
    private readonly Func<IEnumerable<FrameworkElement>> _candidates;  // íconos que aceptan drop
    private readonly Action<string, string> _onDrop;                   // (srcKey, dstKey)

    private FrameworkElement? _pressed;
    private Action? _pressedClick;
    private Point _start;
    private Point _startLocal;   // en coords del contenedor del ícono (puede estar escalado)
    private bool _dragging;
    private bool _ending;
    private FrameworkElement? _hover;
    private Transform? _savedTransform;
    private readonly List<(UIElement el, int z)> _savedZ = new();

    public IconDragReorder(FrameworkElement surface,
                           Func<IEnumerable<FrameworkElement>> candidates,
                           Action<string, string> onDrop)
    {
        _surface = surface;
        _candidates = candidates;
        _onDrop = onDrop;
    }

    /// <summary>True mientras hay un arrastre en curso (el dock no debe colapsar).</summary>
    public bool IsDragging => _dragging;

    /// <summary>Conecta un ícono: el clic se dispara al soltar sin haber arrastrado.</summary>
    public void Attach(FrameworkElement el, string key, Action onClick)
    {
        el.Tag = key;

        el.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressed = el;
            _pressedClick = onClick;
            _start = e.GetPosition(_surface);
            _startLocal = e.GetPosition(ParentOf(el));
            _dragging = false;
            el.CaptureMouse();
            e.Handled = true;
        };

        el.PreviewMouseMove += (_, e) =>
        {
            if (_pressed != el || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(_surface);
            var d = p - _start;

            if (!_dragging)
            {
                if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                BeginDrag(el);
            }

            if (el.RenderTransform is TransformGroup g && g.Children.Count == 2
                && g.Children[1] is TranslateTransform tt)
            {
                // Desplazamiento medido en el espacio del contenedor: si el dock está en
                // tamaño Medium/Slim (contenido escalado), el ícono sigue igual al cursor.
                var dl = e.GetPosition(ParentOf(el)) - _startLocal;
                tt.X = dl.X;
                tt.Y = dl.Y;
            }
            UpdateHover(el, p);
        };

        el.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_pressed != el) return;
            e.Handled = true;
            bool wasDragging = _dragging;
            string? dst = _hover?.Tag as string;

            _ending = true;
            el.ReleaseMouseCapture();
            _ending = false;

            var click = _pressedClick;
            Reset(el);

            if (!wasDragging) { click?.Invoke(); return; }
            if (dst != null && dst != key) _onDrop(key, dst);
        };

        // Captura perdida sin soltar (Alt+Tab, otra ventana, etc.): cancelar el arrastre.
        el.LostMouseCapture += (_, _) =>
        {
            if (_ending || _pressed != el) return;
            Reset(el);
        };
    }

    private IInputElement ParentOf(FrameworkElement el) =>
        VisualTreeHelper.GetParent(el) as IInputElement ?? _surface;

    private void BeginDrag(FrameworkElement el)
    {
        _dragging = true;
        _savedTransform = el.RenderTransform;

        // El ícono "levantado": un poco más grande y semitransparente, siguiendo al cursor.
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1.12, 1.12));
        group.Children.Add(new TranslateTransform());
        el.RenderTransform = group;
        el.Opacity = 0.85;

        // Dibujarlo por encima de los vecinos (y de los contenedores intermedios, ej. la
        // pill de una carpeta abierta en el dock).
        _savedZ.Clear();
        DependencyObject? cur = el;
        while (cur is UIElement ue && cur != _surface)
        {
            if (VisualTreeHelper.GetParent(ue) is Panel)
            {
                _savedZ.Add((ue, Panel.GetZIndex(ue)));
                Panel.SetZIndex(ue, 1000);
            }
            cur = VisualTreeHelper.GetParent(ue);
        }
    }

    private void UpdateHover(FrameworkElement dragged, Point p)
    {
        FrameworkElement? found = null;
        foreach (var c in _candidates())
        {
            if (c == dragged || c.Tag is not string || !c.IsVisible) continue;
            try
            {
                var r = c.TransformToAncestor(_surface).TransformBounds(new Rect(c.RenderSize));
                r.Inflate(4, 4);
                if (r.Contains(p)) { found = c; break; }
            }
            catch { /* no está en el árbol de _surface */ }
        }
        if (found == _hover) return;

        if (_hover != null) Highlight(_hover, false);
        _hover = found;
        if (_hover != null) Highlight(_hover, true);
    }

    /// <summary>Resalta el destino con un anillo del color primario (en el Border del ícono).</summary>
    private static void Highlight(FrameworkElement target, bool on)
    {
        var border = target as Border ?? FindBorder(target);
        if (border == null) { target.Opacity = on ? 0.6 : 1.0; return; }

        if (on)
        {
            border.BorderBrush = (Brush)Application.Current.FindResource("Md3Primary");
            border.BorderThickness = new Thickness(2.5);
        }
        else
        {
            border.ClearValue(Border.BorderBrushProperty);
            border.ClearValue(Border.BorderThicknessProperty);
        }
    }

    private static Border? FindBorder(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            if (VisualTreeHelper.GetChild(root, i) is Border b) return b;
        return null;
    }

    private void Reset(FrameworkElement el)
    {
        if (_dragging)
        {
            if (el.RenderTransform is TransformGroup g && g.Children.Count == 2
                && g.Children[1] is TranslateTransform tt && _savedTransform != null)
            {
                // Volver suave a su lugar (visible si no hubo drop válido; si lo hubo, el
                // rebuild inmediato reemplaza el elemento de todos modos).
                var dur = TimeSpan.FromMilliseconds(160);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var saved = _savedTransform;
                var ax = new DoubleAnimation(0, dur) { EasingFunction = ease };
                ax.Completed += (_, _) => el.RenderTransform = saved;
                tt.BeginAnimation(TranslateTransform.XProperty, ax);
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            }
            else if (_savedTransform != null)
            {
                el.RenderTransform = _savedTransform;
            }
            el.Opacity = 1.0;
            foreach (var (ue, z) in _savedZ) Panel.SetZIndex(ue, z);
            _savedZ.Clear();
        }

        if (_hover != null) Highlight(_hover, false);
        _hover = null;
        _pressed = null;
        _pressedClick = null;
        _dragging = false;
        _savedTransform = null;
    }
}
