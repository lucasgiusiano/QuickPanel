using System.Diagnostics;
using System.Windows.Threading;
using QuickPanel.Services;

namespace QuickPanel.Core;

/// <summary>
/// Detecta ventanas top-level de msedge.exe y mantiene un OverlayManager por cada una.
/// Combina un scan periódico (alta/baja de ventanas) con un WinEvent hook global
/// de LOCATIONCHANGE para seguir movimientos/redimensionados en tiempo real.
/// </summary>
public sealed class EdgeWindowMonitor : IDisposable, IHotkeyTarget
{
    private readonly Dictionary<IntPtr, OverlayManager> _overlays = new();
    private readonly DispatcherTimer? _scanTimer;
    private readonly Win32.WinEventDelegate _locationDelegate; // referencia viva: evita GC del delegate
    private IntPtr _locationHook;
    private IntPtr _lastActiveEdge;
    private readonly Dispatcher _dispatcher;

    /// <summary>Proceso del navegador a monitorear (default de Windows). Null = ninguno compatible.</summary>
    private readonly string? _targetProcess;

    /// <summary>True si el navegador default es compatible (Chromium-like).</summary>
    public bool HasCompatibleBrowser => _targetProcess != null;

    /// <summary>Overlay de la Edge en foco; si no es Edge, la última activa; si no, la primera.</summary>
    public OverlayManager? ActiveOverlay
    {
        get
        {
            var fg = Win32.GetForegroundWindow();
            if (_overlays.TryGetValue(fg, out var o)) { _lastActiveEdge = fg; return o; }
            if (_lastActiveEdge != IntPtr.Zero && _overlays.TryGetValue(_lastActiveEdge, out var last))
                return last;
            return _overlays.Values.FirstOrDefault();
        }
    }

    public EdgeWindowMonitor()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _locationDelegate = OnLocationChanged;

        _targetProcess = BrowserService.DefaultChromiumProcess();
        if (_targetProcess == null) return; // navegador default no compatible: monitor inactivo

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
            {
                _overlays[hwnd] = new OverlayManager(hwnd);
                // Hay navegador compatible visible: precargar íconos en background
                // (una sola vez) para que el primer despliegue del menú sea instantáneo.
                IconCache.Preload();
            }
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

    private bool IsEdgeTopLevel(IntPtr hwnd)
    {
        if (!Win32.IsWindowVisible(hwnd)) return false;
        if (Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero) return false;
        if (Win32.GetClassNameOf(hwnd) != "Chrome_WidgetWin_1") return false;
        if (Win32.GetTitleOf(hwnd).Length == 0) return false;

        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals(_targetProcess, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Destruye y recrea todos los overlays (ej. tras cambiar el modo de menú
    /// Material ↔ Dock, que cambia qué tipo de ventana de control se usa).</summary>
    public void RebuildOverlays()
    {
        foreach (var o in _overlays.Values) o.Dispose();
        _overlays.Clear();
        Scan(); // recrea los overlays para las ventanas de navegador actuales
    }

    public void Dispose()
    {
        _scanTimer?.Stop();
        if (_locationHook != IntPtr.Zero)
        {
            Win32.UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
        foreach (var o in _overlays.Values) o.Dispose();
        _overlays.Clear();
    }
}
