using Microsoft.Win32;

namespace QuickPanel.Services;

/// <summary>
/// Resuelve el navegador predeterminado de Windows y si es un Chromium-like soportado.
/// Se evalúa una sola vez al iniciar la app.
/// </summary>
public static class BrowserService
{
    /// <summary>Nombres de proceso (sin .exe) de los navegadores Chromium soportados.</summary>
    private static readonly Dictionary<string, string> ProgIdToProcess = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MSEdgeHTM"]            = "msedge",   // Edge
        ["MSEdgeDHTML"]         = "msedge",
        ["ChromeHTML"]          = "chrome",   // Chrome
        ["BraveHTML"]           = "brave",    // Brave
        ["BraveFile"]           = "brave",
        ["OperaStable"]         = "opera",    // Opera
        ["VivaldiHTM"]          = "vivaldi",  // Vivaldi
    };

    /// <summary>
    /// Nombre de proceso (sin .exe) del navegador default si es Chromium soportado;
    /// null si es Firefox u otro no compatible.
    /// </summary>
    public static string? DefaultChromiumProcess()
    {
        var progId = ReadDefaultHttpProgId();
        if (string.IsNullOrEmpty(progId)) return null;

        foreach (var (key, proc) in ProgIdToProcess)
            if (progId.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return proc;

        return null;
    }

    /// <summary>Lee el ProgId asociado a http en UserChoice (navegador default del usuario).</summary>
    private static string? ReadDefaultHttpProgId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            return key?.GetValue("ProgId") as string;
        }
        catch { return null; }
    }
}
