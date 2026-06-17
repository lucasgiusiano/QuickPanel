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

    public OverlayManager(IntPtr edgeHwnd)
    {
        EdgeHwnd = edgeHwnd;

        _button = new FloatingButtonWindow(this);
        // El owner nativo (Edge) se asigna dentro del botón en SourceInitialized.
        _button.SetEdgeOwner(edgeHwnd);
        _button.Show();

        Reposition();
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

        if (_appWindows.TryGetValue(app.Id, out var existing))
        {
            existing.ShowAndFocus();
            return;
        }

        var side = PanelGeometry.SideFor(SettingsService.Current.ButtonRelX);

        // Callbacks: capturan el rect ACTUAL del botón en cada llamada,
        // así el panel se re-ancla bien aunque Edge/el botón se muevan.
        PanelGeometry.Rect ButtonRect()
        {
            const double winSize = 64; // ventana del botón (FAB 56 + halo)
            return new PanelGeometry.Rect(_button.Left, _button.Top, winSize, winSize);
        }

        var win = new AppHostWindow(
            app, EdgeHwnd, side, originRelY,
            computeBounds: w => PanelGeometry.Compute(EdgeHwnd, side, w, ButtonRect()),
            maxWidth: () => PanelGeometry.MaxWidth(EdgeHwnd, side, ButtonRect()));

        win.Closed += (_, _) => _appWindows.Remove(app.Id);
        _appWindows[app.Id] = win;
        win.Show();
    }

    public void OpenAddAppDialog()
    {
        CloseMenu();

        if (!LicenseService.CanAddApp(SettingsService.Current.Apps.Count))
        {
            MessageBox.Show(
                $"El plan gratuito permite hasta {LicenseService.FreeAppLimit} apps. " +
                "Pasá a Pro o Complete para agregar apps ilimitadas.",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Information);
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

    public void OpenSettings()
    {
        CloseMenu();
        var win = new SettingsWindow(this);
        win.Show();
    }

    public void EnterMoveMode() => _button.EnterMoveMode();

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
