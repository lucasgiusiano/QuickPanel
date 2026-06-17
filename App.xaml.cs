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
    private WinForms.NotifyIcon? _tray;
    private static System.Threading.Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new System.Threading.Mutex(true, "QuickPanel_SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

        // Sin WebView2 la app no puede mostrar paneles: abortar con aviso.
        if (!Services.WebView2Check.EnsureAvailable()) { Shutdown(); return; }

        SettingsService.Load();
        ThemeService.Apply(SettingsService.Current.SeedColor, SettingsService.Current.ThemeMode);
        // Sincroniza solo si difiere: no re-escribe la clave en cada arranque
        // (evita pisar cambios externos / duplicar la entrada del instalador).
        StartupService.SyncFromPreference(SettingsService.Current.RunAtStartup);

        SetupTray();

        DispatcherUnhandledException += OnUnhandledException;

        _monitor = new EdgeWindowMonitor();
    }

    /// <summary>Re-ancla los paneles abiertos en todas las ventanas de Edge
    /// (ej. tras cambiar el tamaño S/M/L en Configuración).</summary>
    public static void ReanchorAllPanels() =>
        (Current as App)?._monitor?.ReanchorAllPanels();

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
        menu.Items.Add("Configuración", null, (_, _) =>
        {
            var win = new Settings.SettingsWindow(null);
            win.Show();
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[QuickPanel] {e.Exception}");
        e.Handled = true;
    }

    private void ExitApp()
    {
        _monitor?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}
