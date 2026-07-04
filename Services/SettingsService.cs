using System.Globalization;
using System.IO;
using System.Text.Json;
using QuickPanel.Models;
using QuickPanel.Services.CloudSync;

namespace QuickPanel.Services;

public static class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickPanel");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    // Estado local per-device de la última sincronización exitosa. Deliberadamente NO
    // vive en QuickPanelSettings (no se serializa en settings.json ni se sube a la nube):
    // "cuándo sincronizó ESTA PC" es un dato propio de cada equipo. Meterlo en el payload
    // sincronizado haría que dos PCs se pisaran el timestamp indefinidamente y que cada
    // stamp marcara la config como dirty (loop en modo Realtime).
    private static readonly string LastSyncPath = Path.Combine(Dir, "lastsync.txt");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static QuickPanelSettings Current { get; private set; } = new();

    public static string ProfilesDir => Path.Combine(Dir, "Profiles");

    /// <summary>True si hay cambios locales pendientes de subir a la nube. Lo consume el CloudSyncService.</summary>
    public static bool IsDirty { get; private set; }

    /// <summary>Se dispara tras cada Save() con cambios (para debounce en Realtime).</summary>
    public static event Action? Changed;

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<QuickPanelSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { Current = new(); }

        LoadLastSync();
    }

    /// <summary>UTC del último sync exitoso de ESTA PC, o null si nunca sincronizó. Solo lectura;
    /// se actualiza vía <see cref="MarkSyncSuccess"/>.</summary>
    public static DateTime? LastSyncSuccessUtc { get; private set; }

    private static void LoadLastSync()
    {
        try
        {
            if (File.Exists(LastSyncPath) &&
                DateTime.TryParse(File.ReadAllText(LastSyncPath).Trim(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt))
                LastSyncSuccessUtc = dt.ToUniversalTime();
        }
        catch { LastSyncSuccessUtc = null; }
    }

    /// <summary>Registra que esta PC acaba de sincronizar con éxito. Persiste en un archivo
    /// local aparte (no en settings.json, no sube a la nube). La llama el CloudSyncService.</summary>
    public static void MarkSyncSuccess()
    {
        LastSyncSuccessUtc = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(LastSyncPath,
                LastSyncSuccessUtc.Value.ToString("o", CultureInfo.InvariantCulture));
        }
        catch { /* no romper la app por IO */ }
    }

    public static void Save()
    {
        try
        {
            ReconcileJournal(DateTimeOffset.UtcNow);
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts));
            IsDirty = true;
            Changed?.Invoke();
        }
        catch { /* no romper la app por IO */ }
    }

    /// <summary>Marca la config como ya subida (limpia el dirty). La llama el CloudSyncService tras subir.</summary>
    public static void ClearDirty() => IsDirty = false;

    /// <summary>
    /// Alinea el journal con el estado actual de las colecciones:
    /// - Ids nuevos (sin timestamp ni tombstone) → se marcan modificados ahora.
    /// - Ids que ya no están vivos ni tienen tombstone → se marcan borrados ahora.
    /// - Config global → se toca el timestamp global en cada guardado.
    /// Esto cubre los cambios sin exigir que cada punto de edición avise al journal,
    /// aunque igual conviene usar TouchItem/KillItem para timestamps precisos.
    /// </summary>
    private static void ReconcileJournal(DateTimeOffset now)
    {
        var j = Current.SyncJournal;

        var liveIds = Current.Apps.Select(a => a.Id)
            .Concat(Current.Groups.Select(g => g.Id))
            .Concat(Current.ActionHotkeys.Keys)
            .ToHashSet();

        foreach (var id in liveIds)
            if (!j.ItemModifiedUtc.ContainsKey(id))
                j.TouchItem(id, now);

        // Ids que estaban vivos en el journal pero ya no existen → tombstone.
        var vanished = j.ItemModifiedUtc.Keys.Where(id => !liveIds.Contains(id)).ToList();
        foreach (var id in vanished)
            j.KillItem(id, now);

        j.TouchGlobal(now);
        j.PruneTombstones(now);
    }

    /// <summary>Marca explícitamente un ítem (app/grupo/hotkey) como modificado. Uso opcional para timestamps finos.</summary>
    public static void TouchItem(string id) => Current.SyncJournal.TouchItem(id, DateTimeOffset.UtcNow);

    /// <summary>Marca explícitamente un ítem como borrado (tombstone).</summary>
    public static void KillItem(string id) => Current.SyncJournal.KillItem(id, DateTimeOffset.UtcNow);

    /// <summary>Exporta la configuración actual a un archivo JSON.</summary>
    public static void Export(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(Current, JsonOpts));

    /// <summary>
    /// Importa la configuración desde un archivo JSON y la persiste.
    /// Devuelve true si se importó correctamente.
    /// </summary>
    public static bool Import(string path)
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<QuickPanelSettings>(File.ReadAllText(path));
            if (loaded == null) return false;
            Current = loaded;
            Save();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Serializa la configuración actual a JSON (mismo formato que el archivo local).</summary>
    public static string SerializeCurrent() => JsonSerializer.Serialize(Current, JsonOpts);

    /// <summary>Deserializa una config desde JSON sin aplicarla. null si es inválida. Usado por el merge.</summary>
    public static QuickPanelSettings? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<QuickPanelSettings>(json); }
        catch { return null; }
    }

    /// <summary>Reemplaza la config actual en memoria por otra ya construida (ej. resultado de merge) y persiste.</summary>
    public static void Replace(QuickPanelSettings merged)
    {
        Current = merged;
        Directory.CreateDirectory(Dir);
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts)); } catch { }
    }

    /// <summary>
    /// Aplica una configuración recibida como JSON (ej. bajada de la nube) y la persiste.
    /// Devuelve true si se aplicó correctamente. Usado por el Cloud Sync.
    /// </summary>
    public static bool ApplyFromJson(string json)
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<QuickPanelSettings>(json);
            if (loaded == null) return false;
            Current = loaded;
            Save();
            return true;
        }
        catch { return false; }
    }
}
