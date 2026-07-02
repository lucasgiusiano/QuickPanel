using System.IO;

namespace QuickPanel.Services.CloudSync;

/// <summary>
/// Constantes de configuración del Cloud Sync (nombre del archivo remoto, scopes,
/// rutas de caché de tokens). Los Client ID/Secret están en <see cref="CloudSyncSecrets"/>,
/// un archivo separado no versionado (ver CloudSyncSecrets.example.cs).
///
/// Nota de seguridad: en apps de escritorio el client_id — y en Google también el
/// client_secret — NO son confidenciales. La protección real la da PKCE + el hecho de
/// que el redirect es loopback local. No hay backend que comprometer. Igual se separan
/// del código versionado porque los escáneres de secretos (GitHub, etc.) los marcan
/// como tales sin distinguir el contexto de app pública.
/// </summary>
public static partial class CloudSyncConstants
{
    /// <summary>Nombre del archivo tal como se guarda en la nube (appDataFolder / approot).</summary>
    public const string RemoteFileName = "quickpanel-config.json";

    /// <summary>Scope no sensible: acceso solo a la carpeta oculta de la app en el Drive del usuario.</summary>
    public const string GoogleAppDataScope = "https://www.googleapis.com/auth/drive.appdata";

    /// <summary>Authority "common": permite cuentas personales y de organización.</summary>
    public const string MicrosoftAuthority = "https://login.microsoftonline.com/common";

    /// <summary>Scopes delegados de Graph. offline_access habilita el refresh token.</summary>
    public static readonly string[] MicrosoftScopes =
    {
        "Files.ReadWrite.AppFolder",
        "offline_access"
    };

    // ── Rutas locales de caché de credenciales ─────────────────────
    private static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickPanel", "CloudSync");

    /// <summary>Carpeta donde Google.Apis guarda su FileDataStore (tokens de Drive).</summary>
    public static string GoogleTokenStoreDir => Path.Combine(CacheRoot, "google");

    /// <summary>Carpeta de la caché de tokens cifrada de MSAL.</summary>
    public static string MsalCacheDir => Path.Combine(CacheRoot, "msal");

    /// <summary>Nombre del archivo de caché de MSAL.</summary>
    public const string MsalCacheFileName = "msal_cache.bin";
}
