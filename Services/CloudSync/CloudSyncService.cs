using System.Threading;
using QuickPanel.Models;

namespace QuickPanel.Services.CloudSync;

/// <summary>Cómo terminó una operación de sync, para que la UI muestre el mensaje adecuado.</summary>
public enum SyncOutcome
{
    NoProvider,
    UpToDate,
    Uploaded,
    Downloaded,
    Merged,
    NeedsReconcile, // PC nueva: hay algo en la nube y algo local, hay que preguntar
    Conflicts,      // merge con empates ambiguos
    Failed
}

public sealed record SyncRunResult(SyncOutcome Outcome, List<MergeConflict>? Conflicts = null);

/// <summary>
/// Fachada de alto nivel del Cloud Sync (Fase 2: merge por campo + dirty/debounce +
/// intervalos automáticos). Resuelve el proveedor según settings y coordina merge,
/// subida y bajada. El merge en sí vive en <see cref="SyncMerger"/> (puro y testeable).
/// </summary>
public static class CloudSyncService
{
    private static ICloudSyncProvider? _cached;
    private static CloudProviderKind _cachedKind = CloudProviderKind.None;

    private static Timer? _intervalTimer;
    private static Timer? _debounceTimer;
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(5);

    /// <summary>Se dispara cuando un sync automático cambió la config (para refrescar UI/paneles).</summary>
    public static event Action? SyncedInBackground;

    public static ICloudSyncProvider? Current
    {
        get
        {
            var kind = QuickPanelSettings.ParseProvider(SettingsService.Current.CloudProvider);
            if (kind == CloudProviderKind.None) { _cached = null; _cachedKind = kind; return null; }
            if (_cached != null && _cachedKind == kind) return _cached;
            _cached = Create(kind);
            _cachedKind = kind;
            return _cached;
        }
    }

    public static bool IsLinked =>
        Current != null && !string.IsNullOrEmpty(SettingsService.Current.CloudAccount);

    private static ICloudSyncProvider Create(CloudProviderKind kind) => kind switch
    {
        CloudProviderKind.GoogleDrive => new GoogleDriveSyncProvider(),
        CloudProviderKind.OneDrive => new OneDriveSyncProvider(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // ── Vinculación ────────────────────────────────────────────────

    /// <summary>
    /// Vincula un proveedor. Devuelve el email si tuvo éxito, null si canceló.
    /// NO sube ni baja: la primera reconciliación de una PC nueva la maneja
    /// <see cref="FirstSyncAsync"/>, que la UI llama después con confirmación del usuario.
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

    public static async Task UnlinkAsync(CancellationToken ct = default)
    {
        StopAutoSync();
        var provider = Current;
        if (provider != null)
            await provider.UnlinkAsync(ct).ConfigureAwait(false);

        _cached = null;
        _cachedKind = CloudProviderKind.None;
        SettingsService.Current.CloudProvider = CloudProviderKind.None.ToString();
        SettingsService.Current.CloudAccount = "";
        SettingsService.Save();
    }

    /// <summary>
    /// Primera sincronización tras vincular una PC. Si la nube ya tiene un archivo y lo
    /// local no es "vacío por defecto", devuelve NeedsReconcile para que la UI pregunte.
    /// Si la nube está vacía → sube. Si lo local está vacío → baja.
    /// </summary>
    public static async Task<SyncRunResult> FirstSyncAsync(CancellationToken ct = default)
    {
        var provider = Current;
        if (provider == null) return new SyncRunResult(SyncOutcome.NoProvider);

        try
        {
            var meta = await provider.GetMetadataAsync(ct).ConfigureAwait(false);
            bool cloudHasData = meta.Exists && meta.Size > 0;
            bool localHasData = SettingsService.Current.Apps.Count > 0
                             || SettingsService.Current.Groups.Count > 0;

            if (!cloudHasData) { await UploadAsync(provider, ct).ConfigureAwait(false); return new SyncRunResult(SyncOutcome.Uploaded); }
            if (!localHasData) { await DownloadReplaceAsync(provider, ct).ConfigureAwait(false); return new SyncRunResult(SyncOutcome.Downloaded); }

            return new SyncRunResult(SyncOutcome.NeedsReconcile);
        }
        catch { return new SyncRunResult(SyncOutcome.Failed); }
    }

    // ── Operaciones manuales (botones) ─────────────────────────────

    /// <summary>Sube la config local pisando la nube (uso explícito del usuario / reconcile "usar local").</summary>
    public static async Task<SyncRunResult> ForceUploadAsync(CancellationToken ct = default)
    {
        var provider = Current;
        if (provider == null) return new SyncRunResult(SyncOutcome.NoProvider);
        try { await UploadAsync(provider, ct).ConfigureAwait(false); return new SyncRunResult(SyncOutcome.Uploaded); }
        catch { return new SyncRunResult(SyncOutcome.Failed); }
    }

    /// <summary>Baja la config de la nube pisando lo local (reconcile "usar nube").</summary>
    public static async Task<SyncRunResult> ForceDownloadAsync(CancellationToken ct = default)
    {
        var provider = Current;
        if (provider == null) return new SyncRunResult(SyncOutcome.NoProvider);
        try
        {
            bool ok = await DownloadReplaceAsync(provider, ct).ConfigureAwait(false);
            return new SyncRunResult(ok ? SyncOutcome.Downloaded : SyncOutcome.UpToDate);
        }
        catch { return new SyncRunResult(SyncOutcome.Failed); }
    }

    /// <summary>
    /// Sincronización normal: baja lo remoto, mergea por campo con lo local, sube el resultado.
    /// Es la operación por defecto (botón "Sincronizar" y disparos automáticos).
    /// </summary>
    public static async Task<SyncRunResult> SyncAsync(CancellationToken ct = default)
    {
        var provider = Current;
        if (provider == null) return new SyncRunResult(SyncOutcome.NoProvider);

        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
            return new SyncRunResult(SyncOutcome.UpToDate); // ya hay un sync corriendo

        try
        {
            var remoteJson = await provider.DownloadAsync(ct).ConfigureAwait(false);
            if (remoteJson == null)
            {
                await UploadAsync(provider, ct).ConfigureAwait(false);
                return new SyncRunResult(SyncOutcome.Uploaded);
            }

            var remote = SettingsService.Deserialize(remoteJson);
            if (remote == null) return new SyncRunResult(SyncOutcome.Failed);

            var now = DateTimeOffset.UtcNow;
            var result = SyncMerger.Merge(SettingsService.Current, remote, now);

            SettingsService.Replace(result.Merged);
            await UploadAsync(provider, ct).ConfigureAwait(false);

            var outcome = result.HasConflicts ? SyncOutcome.Conflicts : SyncOutcome.Merged;
            return new SyncRunResult(outcome, result.Conflicts);
        }
        catch { return new SyncRunResult(SyncOutcome.Failed); }
        finally { _gate.Release(); }
    }

    // ── Sync automático (intervalos + debounce) ────────────────────

    /// <summary>Arranca el sync automático según el intervalo elegido. Idempotente.</summary>
    public static void StartAutoSync()
    {
        StopAutoSync();
        if (!IsLinked) return;

        var interval = SettingsService.Current.SyncInterval;

        if (interval == SyncInterval.Realtime)
            SettingsService.Changed += OnChangedDebounced;

        var period = interval.TimerPeriod();
        if (period is { } p)
            _intervalTimer = new Timer(_ => FireAndForget(async () => { await SyncAsync().ConfigureAwait(false); }),
                                       null, p, p);
    }

    public static void StopAutoSync()
    {
        SettingsService.Changed -= OnChangedDebounced;
        _intervalTimer?.Dispose(); _intervalTimer = null;
        _debounceTimer?.Dispose(); _debounceTimer = null;
    }

    /// <summary>Sync al cerrar la app, si el intervalo es OnAppClose y hay cambios pendientes.</summary>
    public static async Task SyncOnCloseAsync(CancellationToken ct = default)
    {
        if (!IsLinked) return;
        if (SettingsService.Current.SyncInterval != SyncInterval.OnAppClose) return;
        if (!SettingsService.IsDirty) return;
        await SyncAsync(ct).ConfigureAwait(false);
    }

    private static void OnChangedDebounced()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ => FireAndForget(async () =>
        {
            var r = await SyncAsync().ConfigureAwait(false);
            if (r.Outcome is SyncOutcome.Merged or SyncOutcome.Downloaded)
                SyncedInBackground?.Invoke();
        }), null, DebounceDelay, Timeout.InfiniteTimeSpan);
    }

    // ── Helpers de bajo nivel ──────────────────────────────────────

    private static async Task UploadAsync(ICloudSyncProvider provider, CancellationToken ct)
    {
        var json = SettingsService.SerializeCurrent();
        await provider.UploadAsync(json, ct).ConfigureAwait(false);
        SettingsService.ClearDirty();
    }

    private static async Task<bool> DownloadReplaceAsync(ICloudSyncProvider provider, CancellationToken ct)
    {
        var json = await provider.DownloadAsync(ct).ConfigureAwait(false);
        if (json == null) return false;
        var loaded = SettingsService.Deserialize(json);
        if (loaded == null) return false;
        SettingsService.Replace(loaded);
        SettingsService.ClearDirty();
        return true;
    }

    private static void FireAndForget(Func<Task> op) => _ = Task.Run(op);
}
