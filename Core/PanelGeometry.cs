namespace QuickPanel.Core;

public enum PanelSide { Left, Right }

/// <summary>
/// Calcula la geometría (en DIPs) de un panel de app anclado al menú.
/// Todo es relativo a la VENTANA DE EDGE (no al monitor): el panel se centra
/// verticalmente dentro de la ventana del navegador dejando un gap arriba y abajo
/// (<see cref="VGap"/>) para que se vea flotando, y el borde pegado al menú queda
/// justo antes del botón flotante / la barra del dock para no taparlo.
/// </summary>
public static class PanelGeometry
{
    public const double MinPanel = 320;
    private const double Margin  = 10;

    /// <summary>
    /// Separación vertical (arriba y abajo) entre el panel y los bordes de la ventana
    /// del navegador. Antes el panel ocupaba el alto completo y quedaba "cortado"
    /// contra los bordes; con este gap flota igual que la barra del dock, que usa
    /// exactamente el mismo margen de 16.
    /// </summary>
    private const double VGap = 16;

    /// <summary>Piso de alto para que el panel no colapse ni desborde si la ventana
    /// del navegador es muy chica (en ese caso el gap se sacrifica antes que el panel).</summary>
    private const double MinPanelHeight = 200;

    public readonly record struct Rect(double Left, double Top, double Width, double Height)
    {
        public double Right  => Left + Width;
        public double Bottom => Top + Height;
    }

    public static PanelSide SideFor(double buttonRelX) =>
        buttonRelX >= 0.5 ? PanelSide.Right : PanelSide.Left;

    /// <summary>
    /// Geometría del panel. <paramref name="buttonDip"/> es el rect del botón
    /// flotante en DIPs de pantalla; el panel se ancla justo a su lado.
    /// </summary>
    public static Rect Compute(IntPtr edgeHwnd, PanelSide side, double panelWidthDip, Rect buttonDip)
    {
        Win32.GetWindowRect(edgeHwnd, out var r);
        double scale = Win32.DpiScaleOf(edgeHwnd);

        double topDip       = r.Top    / scale + VGap;
        double heightDip    = Math.Max(MinPanelHeight, (r.Height / scale) - 2 * VGap);
        double edgeLeftDip  = r.Left   / scale;
        double edgeRightDip = r.Right  / scale;

        double left, width;

        if (side == PanelSide.Right)
        {
            double anchorRight = buttonDip.Left - Margin;       // termina antes del botón
            double maxW = anchorRight - (edgeLeftDip + Margin);
            width = Math.Clamp(panelWidthDip, MinPanel, Math.Max(MinPanel, maxW));
            left  = anchorRight - width;
        }
        else
        {
            double anchorLeft = buttonDip.Right + Margin;       // empieza después del botón
            double maxW = (edgeRightDip - Margin) - anchorLeft;
            width = Math.Clamp(panelWidthDip, MinPanel, Math.Max(MinPanel, maxW));
            left  = anchorLeft;
        }

        return new Rect(left, topDip, width, heightDip);
    }

    public static double MaxWidth(IntPtr edgeHwnd, PanelSide side, Rect buttonDip)
    {
        Win32.GetWindowRect(edgeHwnd, out var r);
        double scale = Win32.DpiScaleOf(edgeHwnd);
        double edgeLeftDip  = r.Left  / scale;
        double edgeRightDip = r.Right / scale;

        double maxW = side == PanelSide.Right
            ? (buttonDip.Left - Margin) - (edgeLeftDip + Margin)
            : (edgeRightDip - Margin) - (buttonDip.Right + Margin);

        return Math.Max(MinPanel, maxW);
    }
}
