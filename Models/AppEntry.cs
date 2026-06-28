namespace QuickPanel.Models;

public class AppEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Favicon { get; set; } = "";

    /// <summary>Ruta a un ícono local personalizado. Si está vacío, se usa el favicon.</summary>
    public string IconPath { get; set; } = "";

    /// <summary>Color de acento del círculo en hex. Vacío = color por defecto.</summary>
    public string Color { get; set; } = "";

    /// <summary>Últimas URLs visitadas en esta app. Más reciente primero, máx. 20.</summary>
    public List<string> History { get; set; } = new();

    /// <summary>Atajo de teclado global para abrir esta app.</summary>
    public Hotkey Hotkey { get; set; } = new();

    /// <summary>Factor de zoom recordado para esta app (1.0 = 100%).</summary>
    public double ZoomFactor { get; set; } = 1.0;

    /// <summary>Mantener el panel vivo aunque el Modo Lite esté activo (ej. música de fondo).</summary>
    public bool KeepAlive { get; set; } = false;

    /// <summary>Id del grupo/carpeta al que pertenece. Vacío = suelta en el menú.</summary>
    public string GroupId { get; set; } = "";

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
