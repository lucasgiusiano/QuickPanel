namespace QuickPanel.Core;

public enum PanelSide { Left, Right }

/// <summary>
/// Calcula la geometría (en DIPs) de un panel de app anclado al menú.
/// Todo es relativo a la VENTANA DE EDGE (no al monitor):
/// alto = alto de Edge, top = top de Edge, y el borde pegado al menú queda
/// justo antes del botón flotante para no taparlo.
/// </summary>
public static class PanelGeometry
{
    public const double MinPanel = 320;
    private const double Margin  = 10;

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

        double topDip       = r.Top    / scale;
        double heightDip    = r.Height / scale;
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
