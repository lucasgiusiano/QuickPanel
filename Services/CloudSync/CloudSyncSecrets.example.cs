namespace QuickPanel.Services.CloudSync;

/// <summary>
/// TEMPLATE — copiar a CloudSyncSecrets.cs (gitignoreado) y completar con los valores
/// reales de Partner Center / Google Cloud Console / Entra ID. CloudSyncSecrets.cs NO
/// se versiona; este archivo sí, como referencia de qué campos hacen falta.
/// </summary>
public static partial class CloudSyncConstants
{
    // Cliente OAuth tipo "Desktop app" del proyecto Google Cloud "sidepanel-for-browsers".
    public const string GoogleClientId = "REEMPLAZAR.apps.googleusercontent.com";
    public const string GoogleClientSecret = "REEMPLAZAR";

    // App registrada en Entra ID "SidePanel Desktop" (multi-tenant + cuentas personales).
    public const string MicrosoftClientId = "REEMPLAZAR-guid";
}
