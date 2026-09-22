namespace QuickPanel.Core;

public enum PanelSide { Left, Right }

/// <summary>Dónde va el panel respecto a su ancla.</summary>
public enum PanelPlacement
{
    /// <summary>Al costado (dock izquierdo/derecho o botón flotante): columna de alto completo.</summary>
    Beside,
    /// <summary>Encima de un dock inferior.</summary>
    Above,
    /// <summary>Debajo de un dock superior (solo modo escritorio).</summary>
    Below
}

/// <summary>
/// Calcula la geometría (en DIPs) de un panel de app. Todo es relativo a un rect de
/// referencia (<c>bounds</c>): la ventana del navegador en modo clásico, o el área de trabajo
/// del monitor en modo escritorio — mismo cálculo en ambos casos.
///
/// - <see cref="PanelPlacement.Beside"/>: el panel ocupa el alto de la referencia (con un
///   gap arriba y abajo) y se pega al costado del ancla (barra del dock o botón flotante).
/// - <see cref="PanelPlacement.Above"/>/<see cref="PanelPlacement.Below"/>: dock horizontal.
///   El panel va entre la barra y el borde opuesto, con el borde fijo alineado al ícono que
///   lo abrió (el ancla es el rect de ese ícono proyectado sobre la barra).
///
/// En todos los casos <see cref="PanelSide"/> indica hacia dónde crece el ancho: Right =
/// borde derecho fijo (crece hacia la izquierda, grip a la izquierda); Left = al revés.
/// </summary>
public static class PanelGeometry
{
    public const double MinPanel = 320;
    private const double Margin  = 10;

    /// <summary>Separación entre el panel y los bordes de la referencia (mismo margen de 16
    /// que usa la barra del dock), para que se vea flotando.</summary>
    private const double VGap = 16;

    /// <summary>Piso de alto para que el panel no colapse si la referencia es muy chica.</summary>
    private const double MinPanelHeight = 200;

    public readonly record struct Rect(double Left, double Top, double Width, double Height)
    {
        public double Right   => Left + Width;
        public double Bottom  => Top + Height;
        public double CenterX => Left + Width / 2;
        public double CenterY => Top + Height / 2;
    }

    public static PanelSide SideFor(double buttonRelX) =>
        buttonRelX >= 0.5 ? PanelSide.Right : PanelSide.Left;

    /// <summary>Geometría del panel para el ancho pedido.</summary>
    public static Rect Compute(Rect bounds, PanelPlacement placement, PanelSide side,
                               double panelWidthDip, Rect anchor)
    {
        var (top, height) = Vertical(bounds, placement, anchor);
        var (fixedEdge, maxW) = Horizontal(bounds, placement, side, anchor);

        double width = Math.Clamp(panelWidthDip, MinPanel, Math.Max(MinPanel, maxW));
        double left  = side == PanelSide.Right ? fixedEdge - width : fixedEdge;
        return new Rect(left, top, width, height);
    }

    public static double MaxWidth(Rect bounds, PanelPlacement placement, PanelSide side, Rect anchor)
        => Math.Max(MinPanel, Horizontal(bounds, placement, side, anchor).maxW);

    private static (double top, double height) Vertical(Rect b, PanelPlacement placement, Rect anchor)
    {
        double top, bottom;
        switch (placement)
        {
            case PanelPlacement.Above:
                top    = b.Top + VGap;
                bottom = anchor.Top - Margin;
                break;
            case PanelPlacement.Below:
                top    = anchor.Bottom + Margin;
                bottom = b.Bottom - VGap;
                break;
            default:
                top    = b.Top + VGap;
                bottom = b.Bottom - VGap;
                break;
        }
        double h = Math.Max(MinPanelHeight, bottom - top);
        if (placement == PanelPlacement.Above) top = bottom - h; // sin espacio: crece hacia arriba
        return (top, h);
    }

    /// <summary>Borde fijo del panel (derecho si side=Right, izquierdo si Left) y ancho máximo.</summary>
    private static (double fixedEdge, double maxW) Horizontal(Rect b, PanelPlacement placement,
                                                              PanelSide side, Rect anchor)
    {
        if (placement == PanelPlacement.Beside)
        {
            // Pegado al costado del ancla (termina antes / empieza después).
            if (side == PanelSide.Right)
            {
                double right = anchor.Left - Margin;
                return (right, right - (b.Left + Margin));
            }
            double left = anchor.Right + Margin;
            return (left, (b.Right - Margin) - left);
        }

        // Dock horizontal: borde fijo alineado al ícono, sin salirse de la referencia.
        if (side == PanelSide.Right)
        {
            double right = Math.Min(b.Right - Margin, anchor.Right);
            return (right, right - (b.Left + Margin));
        }
        double l = Math.Max(b.Left + Margin, anchor.Left);
        return (l, (b.Right - Margin) - l);
    }
}
