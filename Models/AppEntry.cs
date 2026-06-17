namespace QuickPanel.Models;

public class AppEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Favicon { get; set; } = "";

    /// <summary>Ruta a un ícono local personalizado (Pro). Si está vacío, se usa el favicon.</summary>
    public string IconPath { get; set; } = "";

    /// <summary>Color de acento del círculo en hex (Complete). Vacío = color por defecto.</summary>
    public string Color { get; set; } = "";

    /// <summary>True si tiene un ícono custom válido en disco.</summary>
    public bool HasCustomIcon =>
        !string.IsNullOrEmpty(IconPath) && System.IO.File.Exists(IconPath);

    public static string FaviconFor(string url)
    {
        try
        {
            var host = new Uri(url).Host;

            // Para subdominios de apps conocidas, usar el dominio raíz da un
            // favicon más confiable (ej. web.whatsapp.com -> whatsapp.com).
            host = NormalizeHost(host);

            return $"https://www.google.com/s2/favicons?sz=64&domain={host}";
        }
        catch { return ""; }
    }

    private static string NormalizeHost(string host)
    {
        // Quitar subdominios comunes que rompen el favicon
        string[] strip = { "web.", "app.", "mail.", "open.", "accounts." };
        foreach (var p in strip)
            if (host.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return host[p.Length..];
        return host;
    }
}
