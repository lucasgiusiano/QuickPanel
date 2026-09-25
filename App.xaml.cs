using System.Windows;
using System.Windows.Threading;
using QuickPanel.Core;
using QuickPanel.Services;

// WinForms solo para NotifyIcon, namespace completo para evitar colisiones
using WinForms = System.Windows.Forms;

namespace QuickPanel;

public partial class App : Application
{
    private EdgeWindowMonitor? _monitor;
    private HotkeyService? _hotkeys;
    private WinForms.NotifyIcon? _tray;
    private static System.Threading.Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new System.Threading.Mutex(true, "QuickPanel_SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        // Idioma: autodetección temprana para que el aviso de WebView2 salga en el
        // idioma del sistema. Tras cargar settings se re-aplica la preferencia guardada.
        Loc.Init(null);

        // Sin WebView2 la app no puede mostrar paneles: abortar con aviso.
        if (!Services.WebView2Check.EnsureAvailable()) { Shutdown(); return; }

        SettingsService.Load();
        Loc.Init(SettingsService.Current.Language);
        ThemeService.Apply(SettingsService.Current.SeedColor, SettingsService.Current.ThemeMode);
        // Sincroniza solo si difiere: no re-escribe la clave/tarea en cada arranque
        // (evita pisar cambios externos / duplicar la entrada del instalador).
        // Fire-and-forget: no bloquea el arranque esperando a WinRT/registro.
        _ = StartupService.SyncFromPreferenceAsync(SettingsService.Current.RunAtStartup);

        SetupTray();

        DispatcherUnhandledException += OnUnhandledException;

        _monitor = new EdgeWindowMonitor();
        _hotkeys = new HotkeyService(_monitor);

        // Cloud Sync: refresca desde la nube al arrancar y activa el modo automático elegido.
        // Con "Solo manual" no se sincroniza nada solo, tampoco al arrancar: únicamente
        // con los botones de Configuración.
        if (Services.CloudSync.CloudSyncService.IsLinked)
        {
            Services.CloudSync.CloudSyncService.SyncedInBackground += OnBackgroundSynced;
            bool manualOnly = SettingsService.Current.SyncInterval
                              == Services.CloudSync.SyncInterval.ManualOnly;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                if (!manualOnly) await Services.CloudSync.CloudSyncService.SyncAsync();
                Services.CloudSync.CloudSyncService.StartAutoSync();
            });
        }

        // En modo escritorio el navegador no importa: la app funciona igual sin uno compatible.
        bool desktop = SettingsService.Current.AnchorMode == Models.AnchorMode.Desktop;
        if (!_monitor.HasCompatibleBrowser && !desktop)
        {
            System.Windows.MessageBox.Show(
                Loc.T("App_ChangeBrowser"),
                "QuickPanel", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        else
        {
            // Solo si hay navegador compatible: no tiene sentido pedir una reseña de una
            // app que el usuario ni siquiera puede usar todavía.
            Services.ReviewPromptService.CheckOnStartup();
        }
    }

    /// <summary>Re-registra los hotkeys tras cambios en Configuración / Administrar apps.</summary>
    public static void ReloadHotkeys() =>
        (Current as App)?._hotkeys?.Reload();

    /// <summary>Re-ancla los paneles abiertos en todas las ventanas de Edge
    /// (ej. tras cambiar el tamaño S/M/L en Configuración).</summary>
    public static void ReanchorAllPanels() =>
        (Current as App)?._monitor?.ReanchorAllPanels();

    /// <summary>Redibuja la lista de apps de todos los docks/menús abiertos (tras reordenar,
    /// editar, agregar o quitar apps), en todas las ventanas de navegador.</summary>
    public static void RefreshAppLists() =>
        (Current as App)?._monitor?.RefreshAppLists();

    /// <summary>Reaplica el layout de todos los docks (tras cambiar su tamaño).</summary>
    public static void RefreshDockLayouts() =>
        (Current as App)?._monitor?.RefreshDockLayouts();

    /// <summary>Cierra (destruye) el panel de una app en todas las ventanas de navegador.
    /// Usado al cambiarle la URL: se vuelve a crear con la nueva al abrirla.</summary>
    public static void CloseAppPanels(string appId) =>
        (Current as App)?._monitor?.CloseAppPanels(appId);

    /// <summary>Overlay activo (ventana de navegador en foco, o monitor bajo el cursor en
    /// modo escritorio). Null si no hay ninguno.</summary>
    public static Core.OverlayManager? ActiveOverlay =>
        (Current as App)?._monitor?.ActiveOverlay;

    /// <summary>Recrea los overlays (ej. tras cambiar el modo de menú en Configuración).</summary>
    public static void RebuildOverlays() =>
        (Current as App)?._monitor?.RebuildOverlays();

    private void SetupTray()
    {
        System.Drawing.Icon icon;
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            icon = System.IO.File.Exists(path)
                ? new System.Drawing.Icon(path)
                : System.Drawing.SystemIcons.Application;
        }
        catch { icon = System.Drawing.SystemIcons.Application; }

        _tray = new WinForms.NotifyIcon
        {
            Icon    = icon,
            Visible = true,
            Text    = "QuickPanel"
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Tray_Settings"), null, (_, _) =>
        {
            var win = new Settings.SettingsWindow(null);
            win.Show();
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Tray_Exit"), null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[QuickPanel] {e.Exception}");
        e.Handled = true;
    }

    private void ExitApp()
    {
        _hotkeys?.Dispose();
        _monitor?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    /// <summary>Un sync automático (intervalo/inicio) trajo cambios: reaplica tema y reancla paneles.</summary>
    private void OnBackgroundSynced() => Dispatcher.Invoke(() =>
    {
        ThemeService.Apply(SettingsService.Current.SeedColor, SettingsService.Current.ThemeMode);
        ReanchorAllPanels();
    });

    protected override void OnExit(ExitEventArgs e)
    {
        // Si el intervalo es "al cerrar" y hay cambios pendientes, subir antes de salir.
        try
        {
            Services.CloudSync.CloudSyncService.SyncOnCloseAsync()
                .Wait(TimeSpan.FromSeconds(10));
        }
        catch { /* no bloquear el cierre por un fallo de red */ }
        Services.CloudSync.CloudSyncService.StopAutoSync();

        _hotkeys?.Dispose();
        _monitor?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}
