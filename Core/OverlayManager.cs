using System.Linq;
using System.Windows;
using QuickPanel.AppWindow;
using QuickPanel.Models;
using QuickPanel.Overlay;
using QuickPanel.Services;
using QuickPanel.Settings;

namespace QuickPanel.Core;

/// <summary>
/// Una instancia por ventana de Edge: botón flotante, menú radial y ventanas de apps.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    public IntPtr EdgeHwnd { get; }

    private readonly FloatingButtonWindow _button;
    private MenuWindow? _menu;
    private readonly Dictionary<string, AppHostWindow> _appWindows = new();
    private bool _disposed;

    private const double ButtonSizeDip = 56;

    private static bool _startAppLaunched;

    public OverlayManager(IntPtr edgeHwnd)
    {
        EdgeHwnd = edgeHwnd;

        _button = new FloatingButtonWindow(this);
        // El owner nativo (Edge) se asigna dentro del botón en SourceInitialized.
        _button.SetEdgeOwner(edgeHwnd);
        _button.Show();

        Reposition();
        TryLaunchStartApp();
    }

    /// <summary>Abre la app configurada como "inicio" una sola vez por sesión,
    /// en la primera ventana de Edge detectada (Pro).</summary>
    private void TryLaunchStartApp()
    {
        if (_startAppLaunched) return;
        var id = SettingsService.Current.StartAppId;
        if (string.IsNullOrEmpty(id)) return;

        var app = SettingsService.Current.Apps.FirstOrDefault(a => a.Id == id);
        if (app == null) return;

        _startAppLaunched = true;
        // Diferido: el botón/overlay recién se está montando.
        _button.Dispatcher.BeginInvoke(new Action(() => OpenApp(app, 0.5)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    // ── Posicionamiento ──

    public void Reposition()
    {
        if (_disposed || !Win32.IsWindow(EdgeHwnd)) return;

        if (Win32.IsIconic(EdgeHwnd))
        {
            CloseMenu();
            return; // owned window: Windows ya la oculta junto al owner
        }

        Win32.GetWindowRect(EdgeHwnd, out var r);
        double scale = Win32.DpiScaleOf(EdgeHwnd);
        var s = SettingsService.Current;

        double btnPx = ButtonSizeDip * scale;
        double pxX = r.Left + s.ButtonRelX * (r.Width - btnPx);
        double pxY = r.Top + s.ButtonRelY * (r.Height - btnPx);

        _button.Left = pxX / scale;
        _button.Top = pxY / scale;

        // Re-anclar paneles abiertos al nuevo rect de Edge
        foreach (var w in _appWindows.Values)
        {
            try { w.Reanchor(); } catch { }
        }

        CloseMenu(); // si Edge se mueve, el menú queda desfasado: se cierra
    }

    /// <summary>Re-ancla los paneles de app abiertos (sin reposicionar el botón
    /// ni cerrar el menú). Usado tras cambiar el tamaño del panel en caliente.</summary>
    public void ReanchorOpenPanels()
    {
        foreach (var w in _appWindows.Values)
        {
            try { w.Reanchor(); } catch { }
        }
    }

    /// <summary>Guarda la posición actual del botón como fracción del rect de Edge.</summary>
    public void SaveButtonPositionFromCurrent()
    {
        Win32.GetWindowRect(EdgeHwnd, out var r);
        double scale = Win32.DpiScaleOf(EdgeHwnd);
        double btnPx = ButtonSizeDip * scale;

        double pxX = _button.Left * scale;
        double pxY = _button.Top * scale;

        var s = SettingsService.Current;
        s.ButtonRelX = Math.Clamp((pxX - r.Left) / Math.Max(1, r.Width - btnPx), 0, 1);
        s.ButtonRelY = Math.Clamp((pxY - r.Top) / Math.Max(1, r.Height - btnPx), 0, 1);
        SettingsService.Save();
    }

    // ── Menú ──

    public void ToggleMenu()
    {
        if (_menu is { IsVisible: true }) { CloseMenu(); return; }

        CloseMenu();
        _menu = new MenuWindow(this, _button);
        _menu.Show();
    }

    public void CloseMenu()
    {
        if (_menu != null)
        {
            try { _menu.Close(); } catch { }
            _menu = null;
        }
    }

    // ── Apps ──

    public void OpenApp(AppEntry app, double originRelY)
    {
        CloseMenu();

        // Un solo panel visible a la vez: ocultar los demás (sin destruirlos,
        // así conservan su caché/sesión de WebView2).
        foreach (var kv in _appWindows)
            if (kv.Key != app.Id && kv.Value.IsVisible)
                kv.Value.HideFromHotkey();

        if (_appWindows.TryGetValue(app.Id, out var existing))
        {
            // Recalcular el lado por si el botón se movió desde la última apertura.
            existing.UpdateSide(PanelGeometry.SideFor(SettingsService.Current.ButtonRelX));
            existing.ShowAndFocus();
            TouchLru(app.Id);
            return;
        }

        // Modo Lite: tope de paneles vivos. Antes de crear uno nuevo, si ya se
        // alcanzó el máximo, matar (ForceClose) el menos usado recientemente.
        EnforceLiveLimit();

        // Callbacks: capturan el rect ACTUAL del botón y el lado ACTUAL en cada
        // llamada, así el panel se re-ancla bien aunque Edge/el botón se muevan.
        PanelGeometry.Rect ButtonRect()
        {
            const double winSize = 64; // ventana del botón (FAB 56 + halo)
            return new PanelGeometry.Rect(_button.Left, _button.Top, winSize, winSize);
        }

        PanelSide CurrentSide() => PanelGeometry.SideFor(SettingsService.Current.ButtonRelX);

        var win = new AppHostWindow(
            app, EdgeHwnd, CurrentSide(), originRelY,
            computeBounds: w => PanelGeometry.Compute(EdgeHwnd, CurrentSide(), w, ButtonRect()),
            maxWidth: () => PanelGeometry.MaxWidth(EdgeHwnd, CurrentSide(), ButtonRect()));

        win.Closed += (_, _) =>
        {
            _appWindows.Remove(app.Id);
            _lru.Remove(app.Id);
            _unread.Remove(app.Id);
            NotifyUnread();
        };
        win.UnreadChanged += w =>
        {
            _unread[w.AppId] = w.Unread;
            NotifyUnread();
        };
        _appWindows[app.Id] = win;
        TouchLru(app.Id);
        win.Show();
    }

    // ── Modo Lite: límite de paneles vivos (LRU) ──

    private const int LiteLiveLimit = 3;
    private readonly List<string> _lru = new(); // más reciente al final

    private void TouchLru(string id)
    {
        _lru.Remove(id);
        _lru.Add(id);
    }

    private void EnforceLiveLimit()
    {
        if (!SettingsService.Current.LiteMode) return;
        // Dejar lugar para el que está por abrirse: mantener (limit - 1) vivos.
        while (_appWindows.Count >= LiteLiveLimit && _lru.Count > 0)
        {
            var oldest = _lru[0];
            _lru.RemoveAt(0);
            if (_appWindows.TryGetValue(oldest, out var w))
            {
                try { w.ForceClose(); } catch { }
            }
        }
    }

    // ── Notificaciones (contador de no leídos) ──

    private readonly Dictionary<string, int> _unread = new();

    /// <summary>No leídos por appId (solo apps con panel abierto).</summary>
    public IReadOnlyDictionary<string, int> Unread => _unread;

    /// <summary>Total de no leídos de todas las apps abiertas.</summary>
    public int UnreadTotal => _unread.Values.Sum();

    /// <summary>Se dispara cuando cambia algún contador (lo escuchan botón y menú).</summary>
    public event Action? UnreadUpdated;

    private void NotifyUnread()
    {
        bool show = SettingsService.Current.ShowBadges && LicenseService.HasFeature(Feature.Notifications);
        _button.SetBadge(show ? UnreadTotal : 0);
        UnreadUpdated?.Invoke();
    }

    public void OpenAddAppDialog()
    {
        CloseMenu();

        if (!LicenseService.CanAddApp(SettingsService.Current.Apps.Count))
        {
            new UpgradeWindow(
                $"Llegaste al límite de {LicenseService.FreeAppLimit} apps del plan Free. " +
                "Pasá a Pro para agregar apps ilimitadas.").ShowDialog();

            // Si compró/cambió de plan, continuar con el alta.
            if (!LicenseService.CanAddApp(SettingsService.Current.Apps.Count))
                return;
        }

        var dlg = new AddAppDialog();
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            SettingsService.Current.Apps.Add(dlg.Result);
            SettingsService.Save();
        }
    }

    public void RemoveApp(AppEntry app)
    {
        SettingsService.Current.Apps.RemoveAll(a => a.Id == app.Id);
        SettingsService.Save();
        if (_appWindows.TryGetValue(app.Id, out var w))
        {
            w.ForceClose();
            _appWindows.Remove(app.Id);
        }
        CloseMenu();
    }

    private SettingsWindow? _settingsWin;

    public void OpenSettings()
    {
        CloseMenu();

        // Si ya hay una ventana de Configuración abierta, traerla al frente
        // en vez de abrir otra.
        if (_settingsWin != null)
        {
            _settingsWin.Activate();
            return;
        }

        _settingsWin = new SettingsWindow(this);
        _settingsWin.Closed += (_, _) => _settingsWin = null;
        _settingsWin.Show();
    }

    public void EnterMoveMode() => _button.EnterMoveMode();

    // ── Acciones para hotkeys ──

    /// <summary>Abre/enfoca una app por su Id (usado por hotkeys).</summary>
    public void OpenAppById(string id)
    {
        var app = SettingsService.Current.Apps.FirstOrDefault(a => a.Id == id);
        if (app != null) OpenApp(app, 0.5);
    }

    /// <summary>Oculta el panel de app actualmente activo/visible (usado por hotkey).</summary>
    public void HideActivePanel()
    {
        var w = _appWindows.Values.FirstOrDefault(x => x.IsActive)
             ?? _appWindows.Values.FirstOrDefault(x => x.IsVisible);
        w?.HideFromHotkey();
    }

    /// <summary>Activa/desactiva el auto-ocultar del botón flotante (usado por hotkey).</summary>
    public void ToggleAutoHide()
    {
        SettingsService.Current.AutoHide = !SettingsService.Current.AutoHide;
        SettingsService.Save();
    }

    /// <summary>Cicla a la app siguiente (+1) o anterior (-1) en la lista (usado por hotkey).</summary>
    public void CycleApp(int dir)
    {
        var apps = SettingsService.Current.Apps;
        if (apps.Count == 0) return;

        // Punto de partida: la app visible actual, o la primera.
        var currentId = _appWindows.Values.FirstOrDefault(x => x.IsVisible)?.AppId;
        int idx = currentId != null ? apps.FindIndex(a => a.Id == currentId) : -1;
        int next = ((idx + dir) % apps.Count + apps.Count) % apps.Count;
        OpenApp(apps[next], 0.5);
    }

    public void Dispose()
    {
        _disposed = true;
        CloseMenu();
        foreach (var w in _appWindows.Values)
        {
            try { w.ForceClose(); } catch { }
        }
        _appWindows.Clear();
        try { _button.Close(); } catch { }
    }
}
