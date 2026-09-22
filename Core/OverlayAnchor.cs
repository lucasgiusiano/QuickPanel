using QuickPanel.Models;
using QuickPanel.Services;
using WinScreen = System.Windows.Forms.Screen;

namespace QuickPanel.Core;

/// <summary>
/// Aquello a lo que se ancla un <see cref="OverlayManager"/>: la ventana de un navegador
/// (modo clásico) o el área de trabajo de un monitor (modo escritorio). Toda la geometría
/// (dock, botón flotante, menú, paneles) se calcula contra <see cref="BoundsDip"/>, así que
/// el resto de la app no necesita saber en qué modo está.
/// </summary>
public abstract class OverlayAnchor
{
    /// <summary>True en modo escritorio.</summary>
    public abstract bool IsDesktop { get; }

    /// <summary>Ventana dueña de las ventanas del overlay (el navegador), o Zero en escritorio.</summary>
    public abstract IntPtr OwnerHwnd { get; }

    /// <summary>False si la ventana/monitor ya no existe.</summary>
    public abstract bool IsAlive { get; }

    /// <summary>True si el navegador está minimizado (en escritorio, nunca).</summary>
    public abstract bool IsMinimized { get; }

    /// <summary>Rect de referencia en DIPs: ventana del navegador o área de trabajo del monitor.</summary>
    public abstract PanelGeometry.Rect BoundsDip { get; }

    /// <summary>Hay contenido en pantalla completa (video/F11) sobre esta ancla.</summary>
    public abstract bool IsFullscreen { get; }

    /// <summary>
    /// Cuánto se "desborda" la ventana de referencia más allá de lo visible, en DIPs. Una
    /// ventana de navegador maximizada tiene ~7-8px invisibles por lado (borde de DWM) que
    /// cuentan en GetWindowRect; el área de trabajo de un monitor no tiene ninguno.
    /// </summary>
    public abstract double EdgeOverflow { get; }

    /// <summary>Clave para las posiciones recordadas: "browser" o el nombre del monitor.</summary>
    public abstract string PositionKey { get; }

    /// <summary>Borde efectivo del dock para esta ancla (Top solo existe en escritorio).</summary>
    public DockEdge DockEdge
    {
        get
        {
            var s = SettingsService.Current;
            if (IsDesktop) return s.DesktopDockEdge;
            return s.DockEdge == DockEdge.Top ? DockEdge.Right : s.DockEdge;
        }
    }

    // ── Posición del botón flotante (Material), relativa a BoundsDip ──

    public (double X, double Y) ButtonRel
    {
        get
        {
            var s = SettingsService.Current;
            if (!IsDesktop) return (s.ButtonRelX, s.ButtonRelY);
            return s.DesktopButtonPositions.TryGetValue(PositionKey, out var p) ? (p.X, p.Y) : (0.97, 0.92);
        }
        set
        {
            var s = SettingsService.Current;
            if (!IsDesktop) { s.ButtonRelX = value.X; s.ButtonRelY = value.Y; return; }
            s.DesktopButtonPositions[PositionKey] = new RelPoint { X = value.X, Y = value.Y };
        }
    }

    // ── Posición de la pestaña del dock a lo largo de su borde (0..1) ──

    private string HandleKey => $"{PositionKey}|{DockEdge}";

    public double HandlePosition
    {
        get => SettingsService.Current.DockHandlePositions.TryGetValue(HandleKey, out var v)
            ? Math.Clamp(v, 0, 1) : 0.5;
        set => SettingsService.Current.DockHandlePositions[HandleKey] = Math.Clamp(value, 0, 1);
    }
}

/// <summary>Ancla clásica: una ventana top-level del navegador.</summary>
public sealed class BrowserAnchor : OverlayAnchor
{
    private readonly IntPtr _hwnd;
    public BrowserAnchor(IntPtr hwnd) => _hwnd = hwnd;

    public override bool IsDesktop => false;
    public override IntPtr OwnerHwnd => _hwnd;
    public override bool IsAlive => Win32.IsWindow(_hwnd);
    public override bool IsMinimized => Win32.IsIconic(_hwnd);
    public override bool IsFullscreen => Win32.IsFullscreen(_hwnd);
    public override double EdgeOverflow => 7;
    public override string PositionKey => "browser";

    public override PanelGeometry.Rect BoundsDip
    {
        get
        {
            Win32.GetWindowRect(_hwnd, out var r);
            double sc = Win32.DpiScaleOf(_hwnd);
            return new PanelGeometry.Rect(r.Left / sc, r.Top / sc, r.Width / sc, r.Height / sc);
        }
    }
}

/// <summary>
/// Ancla de escritorio: el área de trabajo (sin la barra de tareas) de un monitor, igual
/// que una ventana maximizada. El monitor se resuelve por su nombre de dispositivo en cada
/// consulta, así un cambio de resolución o de barra de tareas se refleja solo.
/// </summary>
public sealed class MonitorAnchor : OverlayAnchor
{
    public string DeviceName { get; }
    public MonitorAnchor(string deviceName) => DeviceName = deviceName;

    private WinScreen? Screen =>
        WinScreen.AllScreens.FirstOrDefault(s => s.DeviceName == DeviceName);

    public override bool IsDesktop => true;
    public override IntPtr OwnerHwnd => IntPtr.Zero;
    public override bool IsAlive => Screen != null;
    public override bool IsMinimized => false;
    public override double EdgeOverflow => 0;
    public override string PositionKey => DeviceName;

    /// <summary>Área de trabajo en píxeles físicos (vacía si el monitor ya no está).</summary>
    public System.Drawing.Rectangle WorkAreaPx => Screen?.WorkingArea ?? System.Drawing.Rectangle.Empty;

    public double Scale
    {
        get
        {
            var wa = WorkAreaPx;
            return Win32.DpiScaleOfPoint(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2);
        }
    }

    public override PanelGeometry.Rect BoundsDip
    {
        get
        {
            var wa = WorkAreaPx;
            double sc = Scale;
            return new PanelGeometry.Rect(wa.Left / sc, wa.Top / sc, wa.Width / sc, wa.Height / sc);
        }
    }

    /// <summary>
    /// La ventana en primer plano (de otra app) está en pantalla completa sobre ESTE monitor:
    /// un video a pantalla completa en el navegador, un reproductor, un juego...
    /// </summary>
    public override bool IsFullscreen
    {
        get
        {
            var fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            Win32.GetWindowThreadProcessId(fg, out uint pid);
            if ((int)pid == Environment.ProcessId) return false;
            // El escritorio (Progman/WorkerW) cubre el monitor completo pero no es "pantalla completa".
            var cls = Win32.GetClassNameOf(fg);
            if (cls is "Progman" or "WorkerW") return false;
            if (!Win32.IsFullscreen(fg)) return false;

            Win32.GetWindowRect(fg, out var r);
            var scr = Screen;
            return scr != null && scr.Bounds.Contains(r.Left + r.Width / 2, r.Top + r.Height / 2);
        }
    }

    /// <summary>
    /// Monitores a usar según la preferencia. Siempre devuelve al menos uno (el principal):
    /// "Secundario" con un solo monitor cae al principal.
    /// </summary>
    public static IEnumerable<string> ResolveDevices(DesktopMonitors pref)
    {
        var all = WinScreen.AllScreens;
        var primary = WinScreen.PrimaryScreen ?? all.First();
        switch (pref)
        {
            case DesktopMonitors.All:
                // Principal primero, el resto de izquierda a derecha.
                return all.OrderBy(s => s.Primary ? 0 : 1).ThenBy(s => s.Bounds.Left)
                          .Select(s => s.DeviceName).ToList();
            case DesktopMonitors.Secondary:
                var second = all.Where(s => !s.Primary).OrderBy(s => s.Bounds.Left).FirstOrDefault();
                return new[] { (second ?? primary).DeviceName };
            default:
                return new[] { primary.DeviceName };
        }
    }

    public static int MonitorCount => WinScreen.AllScreens.Length;
}
