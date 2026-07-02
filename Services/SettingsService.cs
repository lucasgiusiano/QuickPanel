using System.IO;
using System.Text.Json;
using QuickPanel.Models;

namespace QuickPanel.Services;

public static class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickPanel");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static QuickPanelSettings Current { get; private set; } = new();

    public static string ProfilesDir => Path.Combine(Dir, "Profiles");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<QuickPanelSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { Current = new(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts));
        }
        catch { /* no romper la app por IO */ }
    }

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
