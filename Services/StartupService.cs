using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace QuickPanel.Services;

public static class StartupService
{
    private const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "QuickPanel";

    /// <summary>
    /// Id de la tarea declarada en Package.appxmanifest (proyecto QuickPanel.Package,
    /// fuera de este repo). DEBE coincidir exactamente con el atributo TaskId del
    /// &lt;uap5:StartupTask&gt; — ver nota al final de este archivo con el XML exacto
    /// a agregar al manifest. Sin esa declaración, StartupTask.GetAsync lanza una
    /// excepción que queda absorbida por los catch de abajo (no-op silencioso).
    /// </summary>
    private const string StartupTaskId = "QuickPanelStartupTask";

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

    /// <summary>¿Está actualmente configurado el inicio automático?</summary>
    public static async Task<bool> IsEnabledAsync()
    {
        if (IsPackaged)
        {
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            catch
            {
                // Extensión no declarada todavía en el manifest, u otro fallo de WinRT.
                return false;
            }
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public static async Task SetRunAtStartupAsync(bool enable)
    {
        if (IsPackaged)
        {
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                if (enable)
                {
                    // Solo pedir habilitación si está en el estado neutro "Disabled".
                    // Si el usuario o una política ya lo deshabilitaron explícitamente
                    // (DisabledByUser / DisabledByPolicy), RequestEnableAsync no puede
                    // saltarse esa decisión del sistema — Windows ignora el pedido.
                    if (task.State == StartupTaskState.Disabled)
                        await task.RequestEnableAsync();
                }
                else
                {
                    task.Disable();
                }
            }
            catch
            {
                // Extensión no declarada en el manifest: no-op silencioso, igual que
                // el comportamiento anterior (sin StartupTask implementado).
            }
            return;
        }

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
    /// Sincroniza el estado real con la preferencia guardada SOLO si difieren.
    /// Evita re-escribir la clave/tarea en cada arranque.
    /// </summary>
    public static async Task SyncFromPreferenceAsync(bool desired)
    {
        if (await IsEnabledAsync() != desired)
            await SetRunAtStartupAsync(desired);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}

// ─────────────────────────────────────────────────────────────────────────────
// PENDIENTE FUERA DE ESTE REPO: declarar la extensión en Package.appxmanifest
// (proyecto QuickPanel.Package / .wapproj). Sin esto, IsPackaged sigue
// funcionando, pero StartupTask.GetAsync siempre falla (no-op silencioso).
//
// 1) En la raíz <Package>, agregar el namespace y declararlo ignorable:
//      xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
//      <IgnorableNamespaces>... uap5</IgnorableNamespaces>
//
// 2) Dentro de <Applications><Application Id="App" ...>, agregar:
//      <Extensions>
//        <uap5:Extension Category="windows.startupTask">
//          <uap5:StartupTask
//            TaskId="QuickPanelStartupTask"
//            Enabled="false"
//            DisplayName="SidePanel for Edge" />
//        </uap5:Extension>
//      </Extensions>
//
//    TaskId debe coincidir EXACTO con StartupTaskId de esta clase.
//    Esto se edita en Visual Studio (doble clic en Package.appxmanifest → vista
//    de diseñador, o "View Code" para editarlo a mano).
// ─────────────────────────────────────────────────────────────────────────────
