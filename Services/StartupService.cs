using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace QuickPanel.Services;

public static class StartupService
{
    private const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "QuickPanel";

    /// <summary>
    /// True si la app corre dentro de un paquete MSIX (instalada desde Store).
    /// En ese caso la clave Run del registro está virtualizada y NO produce
    /// inicio automático real: hay que usar StartupTask declarado en el manifest.
    /// </summary>
    public static bool IsPackaged
    {
        get
        {
            int len = 0;
            // APPMODEL_ERROR_NO_PACKAGE = 15700 → no empaquetado
            int rc = GetCurrentPackageFullName(ref len, null);
            return rc != 15700;
        }
    }

    /// <summary>¿Está actualmente configurado el inicio automático? (solo no empaquetado)</summary>
    public static bool IsEnabled()
    {
        if (IsPackaged) return false; // el estado real lo gestiona Windows vía StartupTask
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public static void SetRunAtStartup(bool enable)
    {
        // En MSIX el toggle real necesita la API Windows.ApplicationModel.StartupTask
        // (requiere WinRT). Acá no tocamos el registro porque sería un no-op virtualizado.
        if (IsPackaged) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(AppName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch { /* sin permisos: ignorar */ }
    }

    /// <summary>
    /// Sincroniza el estado del registro con la preferencia guardada SOLO si difieren.
    /// Evita re-escribir la clave en cada arranque (lo que pisaba cambios externos
    /// o duplicaba la entrada creada por el instalador Inno Setup).
    /// </summary>
    public static void SyncFromPreference(bool desired)
    {
        if (IsPackaged) return;
        if (IsEnabled() != desired)
            SetRunAtStartup(desired);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
