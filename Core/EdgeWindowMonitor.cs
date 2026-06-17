using System.Diagnostics;
using System.Windows.Threading;

namespace QuickPanel.Core;

/// <summary>
/// Detecta ventanas top-level de msedge.exe y mantiene un OverlayManager por cada una.
/// Combina un scan periódico (alta/baja de ventanas) con un WinEvent hook global
/// de LOCATIONCHANGE para seguir movimientos/redimensionados en tiempo real.
/// </summary>
public sealed class EdgeWindowMonitor : IDisposable
{
    private readonly Dictionary<IntPtr, OverlayManager> _overlays = new();
    private readonly DispatcherTimer _scanTimer;
    private readonly Win32.WinEventDelegate _locationDelegate; // referencia viva: evita GC del delegate
    private IntPtr _locationHook;
    private readonly Dispatcher _dispatcher;

    public EdgeWindowMonitor()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _locationDelegate = OnLocationChanged;

        _locationHook = Win32.SetWinEventHook(
            Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _locationDelegate, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _scanTimer.Tick += (_, _) => Scan();
        _scanTimer.Start();
        Scan();
    }

    private void Scan()
    {
        var found = new HashSet<IntPtr>();

        Win32.EnumWindows((hwnd, _) =>
        {
            if (IsEdgeTopLevel(hwnd)) found.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        // Nuevas ventanas
        foreach (var hwnd in found)
        {
            if (!_overlays.ContainsKey(hwnd))
                _overlays[hwnd] = new OverlayManager(hwnd);
        }

        // Ventanas cerradas
        foreach (var hwnd in _overlays.Keys.ToList())
        {
            if (!found.Contains(hwnd) || !Win32.IsWindow(hwnd))
            {
                _overlays[hwnd].Dispose();
                _overlays.Remove(hwnd);
            }
        }
    }

    private static bool IsEdgeTopLevel(IntPtr hwnd)
    {
        if (!Win32.IsWindowVisible(hwnd)) return false;
        if (Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero) return false;
        if (Win32.GetClassNameOf(hwnd) != "Chrome_WidgetWin_1") return false;
        if (Win32.GetTitleOf(hwnd).Length == 0) return false;

        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void OnLocationChanged(IntPtr hook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Win32.OBJID_WINDOW) return;
        if (!_overlays.TryGetValue(hwnd, out var overlay)) return;

        // El hook llega en el hilo del message loop que lo registró (UI), pero
        // BeginInvoke desacopla y absorbe ráfagas durante drags rápidos.
        _dispatcher.BeginInvoke(DispatcherPriority.Render, overlay.Reposition);
    }

    /// <summary>Re-aplica posición/tamaño en todos los overlays (ej. tras cambiar settings).</summary>
    public void RepositionAll()
    {
        foreach (var o in _overlays.Values) o.Reposition();
    }

    /// <summary>Re-ancla solo los paneles de app abiertos, sin tocar el botón ni el menú
    /// (ej. tras cambiar el tamaño S/M/L).</summary>
    public void ReanchorAllPanels()
    {
        foreach (var o in _overlays.Values) o.ReanchorOpenPanels();
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        if (_locationHook != IntPtr.Zero)
        {
            Win32.UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
        foreach (var o in _overlays.Values) o.Dispose();
        _overlays.Clear();
    }
}
