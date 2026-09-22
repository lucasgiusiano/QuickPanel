using System.Diagnostics;
using System.Windows.Threading;
using QuickPanel.Models;
using QuickPanel.Services;

namespace QuickPanel.Core;

/// <summary>
/// Mantiene los OverlayManager según el modo de anclaje:
/// - Navegador: detecta ventanas top-level del navegador y mantiene un overlay por cada una.
///   Combina un scan periódico (alta/baja de ventanas) con un WinEvent hook global de
///   LOCATIONCHANGE para seguir movimientos/redimensionados en tiempo real.
/// - Escritorio: un overlay por monitor elegido (principal, secundario o todos), anclado a
///   su área de trabajo. Ningún navegador genera overlay. Se recrean si cambia la
///   configuración de pantallas (monitor conectado/desconectado, resolución).
/// </summary>
public sealed class EdgeWindowMonitor : IDisposable, IHotkeyTarget
{
    private readonly Dictionary<IntPtr, OverlayManager> _overlays = new();
    private readonly List<OverlayManager> _desktopOverlays = new();
    private readonly DispatcherTimer? _scanTimer;
    private readonly DispatcherTimer _desktopTimer;

    private static bool DesktopMode => SettingsService.Current.AnchorMode == AnchorMode.Desktop;

    /// <summary>Todos los overlays vivos, de cualquier modo.</summary>
    private IEnumerable<OverlayManager> AllOverlays => _overlays.Values.Concat(_desktopOverlays);
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
            // Escritorio: el del monitor donde está el cursor; si no, el primero.
            if (_desktopOverlays.Count > 0)
                return _desktopOverlays.FirstOrDefault(o => o.ContainsCursor()) ?? _desktopOverlays[0];

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

        // Modo escritorio: sin ventana de navegador que avise con LOCATIONCHANGE, un tick
        // periódico detecta pantalla completa y cambios del área de trabajo.
        _desktopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _desktopTimer.Tick += (_, _) =>
        {
            foreach (var o in _desktopOverlays) o.DesktopTick();
        };
        _desktopTimer.Start();

        // Monitor conectado/desconectado o cambio de resolución: rearmar los de escritorio.
        // El evento llega desde otro hilo: pasar al de UI.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (DesktopMode) BuildDesktopOverlays();

        _targetProcess = BrowserService.DefaultChromiumProcess();
        if (_targetProcess == null) return; // navegador default no compatible: sin modo navegador

        _locationHook = Win32.SetWinEventHook(
            Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _locationDelegate, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _scanTimer.Tick += (_, _) => Scan();
        _scanTimer.Start();
        Scan();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            if (!DesktopMode) return;
            DisposeDesktopOverlays();
            BuildDesktopOverlays();
        });

    /// <summary>Crea un overlay por cada monitor elegido en Configuración.</summary>
    private void BuildDesktopOverlays()
    {
        foreach (var device in MonitorAnchor.ResolveDevices(SettingsService.Current.DesktopMonitors))
            _desktopOverlays.Add(new OverlayManager(new MonitorAnchor(device)));
        IconCache.Preload();
    }

    private void DisposeDesktopOverlays()
    {
        foreach (var o in _desktopOverlays) o.Dispose();
        _desktopOverlays.Clear();
    }

    private void Scan()
    {
        // Escritorio y navegador son excluyentes: en escritorio no se anclan navegadores.
        if (DesktopMode) return;

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
                _overlays[hwnd] = new OverlayManager(new BrowserAnchor(hwnd));
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
        foreach (var o in AllOverlays) o.Reposition();
    }

    /// <summary>Re-ancla solo los paneles de app abiertos, sin tocar el botón ni el menú
    /// (ej. tras cambiar el tamaño S/M/L).</summary>
    public void ReanchorAllPanels()
    {
        foreach (var o in AllOverlays) o.ReanchorOpenPanels();
    }

    /// <summary>Redibuja la lista de apps en los docks/menús de todas las ventanas.</summary>
    public void RefreshAppLists()
    {
        foreach (var o in AllOverlays) o.RefreshApps();
    }

    /// <summary>Cierra el panel de una app en todas las ventanas.</summary>
    public void CloseAppPanels(string appId)
    {
        foreach (var o in AllOverlays) o.ClosePanel(appId);
    }

    /// <summary>Destruye y recrea todos los overlays (ej. tras cambiar el modo de menú
    /// Material ↔ Dock, que cambia qué tipo de ventana de control se usa).</summary>
    public void RebuildOverlays()
    {
        foreach (var o in _overlays.Values) o.Dispose();
        _overlays.Clear();
        DisposeDesktopOverlays();

        if (DesktopMode) BuildDesktopOverlays();
        else Scan(); // recrea los overlays para las ventanas de navegador actuales
    }

    public void Dispose()
    {
        _scanTimer?.Stop();
        _desktopTimer.Stop();
        // Evento estático: sin esto el monitor queda vivo colgado de SystemEvents.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        DisposeDesktopOverlays();
        if (_locationHook != IntPtr.Zero)
        {
            Win32.UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
        foreach (var o in _overlays.Values) o.Dispose();
        _overlays.Clear();
    }
}
