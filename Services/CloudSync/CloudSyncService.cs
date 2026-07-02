namespace QuickPanel.Services.CloudSync;

/// <summary>
/// Fachada de alto nivel para el Cloud Sync (Fase 1: manual, sin merge).
///
/// Resuelve el proveedor concreto según <see cref="QuickPanelSettings.CloudProvider"/>,
/// y expone operaciones simples que la UI de Configuración llama directamente:
/// vincular, desvincular, subir la config local, bajar la config de la nube.
///
/// La lógica de conflicto (merge por campo, tombstones, dirty+debounce) llega en Fase 2;
/// acá no se resuelven colisiones: subir pisa la nube, bajar pisa lo local.
/// </summary>
public static class CloudSyncService
{
    private static ICloudSyncProvider? _cached;
    private static CloudProviderKind _cachedKind = CloudProviderKind.None;

    /// <summary>Proveedor actualmente configurado (según settings). null si no hay ninguno.</summary>
    public static ICloudSyncProvider? Current
    {
        get
        {
            var kind = Models.QuickPanelSettings.ParseProvider(SettingsService.Current.CloudProvider);
            if (kind == CloudProviderKind.None) { _cached = null; _cachedKind = kind; return null; }
            if (_cached != null && _cachedKind == kind) return _cached;

            _cached = Create(kind);
            _cachedKind = kind;
            return _cached;
        }
    }

    private static ICloudSyncProvider Create(CloudProviderKind kind) => kind switch
    {
        CloudProviderKind.GoogleDrive => new GoogleDriveSyncProvider(),
        CloudProviderKind.OneDrive => new OneDriveSyncProvider(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// Vincula un proveedor: dispara el OAuth y, si tiene éxito, lo persiste en settings.
    /// Devuelve el email de la cuenta, o null si el usuario canceló.
    /// </summary>
    public static async Task<string?> LinkAsync(CloudProviderKind kind, CancellationToken ct = default)
    {
        var provider = Create(kind);
        var account = await provider.AuthenticateAsync(ct).ConfigureAwait(false);
        if (account == null) return null;

        _cached = provider;
        _cachedKind = kind;
        SettingsService.Current.CloudProvider = kind.ToString();
        SettingsService.Current.CloudAccount = account;
        SettingsService.Save();
        return account;
    }

    /// <summary>Desvincula el proveedor actual y limpia settings.</summary>
    public static async Task UnlinkAsync(CancellationToken ct = default)
    {
        var provider = Current;
        if (provider != null)
            await provider.UnlinkAsync(ct).ConfigureAwait(false);

        _cached = null;
        _cachedKind = CloudProviderKind.None;
        SettingsService.Current.CloudProvider = CloudProviderKind.None.ToString();
        SettingsService.Current.CloudAccount = "";
        SettingsService.Save();
    }

    /// <summary>Sube la configuración local actual a la nube (pisa lo que haya). Requiere proveedor vinculado.</summary>
    public static async Task UploadCurrentAsync(CancellationToken ct = default)
    {
        var provider = Current ?? throw new InvalidOperationException("No hay proveedor de sync vinculado.");
        var json = SettingsService.SerializeCurrent();
        await provider.UploadAsync(json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Baja la configuración de la nube y la aplica localmente (pisa lo local).
    /// Devuelve false si no había archivo en la nube.
    /// </summary>
    public static async Task<bool> DownloadAndApplyAsync(CancellationToken ct = default)
    {
        var provider = Current ?? throw new InvalidOperationException("No hay proveedor de sync vinculado.");
        var json = await provider.DownloadAsync(ct).ConfigureAwait(false);
        if (json == null) return false;

        return SettingsService.ApplyFromJson(json);
    }
}
