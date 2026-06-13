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
}
