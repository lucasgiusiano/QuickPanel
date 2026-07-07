using System.Runtime.InteropServices;
using System.Text;
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

        // ProgId reconocido pero no está en la tabla: intentar resolver por el .exe
        // asociado (cubre variantes de ProgId no contempladas, p. ej. canales beta/dev).
        var exe = ProcessFromAssociatedExe();
        return exe;
    }

    /// <summary>
    /// Lee el ProgId asociado a http. Usa primero la API oficial de Windows
    /// (AssocQueryString con ASSOCSTR_PROGID), que es la misma que consulta el
    /// Explorador para resolver el navegador default y funciona de forma fiable
    /// también desde un paquete MSIX. Si por alguna razón no devuelve nada, cae
    /// a la lectura directa de UserChoice en el registro como respaldo.
    /// </summary>
    private static string? ReadDefaultHttpProgId()
    {
        var viaApi = QueryAssocString(AssocStr.ProgID, "http");
        if (!string.IsNullOrEmpty(viaApi)) return viaApi;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            return key?.GetValue("ProgId") as string;
        }
        catch { return null; }
    }

    /// <summary>
    /// Resuelve el ejecutable asociado al esquema http vía AssocQueryString
    /// (ASSOCSTR_EXECUTABLE) y lo mapea a uno de los procesos soportados por el
    /// nombre del .exe. Último recurso cuando el ProgId no coincide con la tabla.
    /// </summary>
    private static string? ProcessFromAssociatedExe()
    {
        var exePath = QueryAssocString(AssocStr.Executable, "http");
        if (string.IsNullOrEmpty(exePath)) return null;

        var name = Path.GetFileNameWithoutExtension(exePath);
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var proc in ProgIdToProcess.Values)
            if (name.Equals(proc, StringComparison.OrdinalIgnoreCase))
                return proc;

        // launcher.exe de Opera (opera.exe vive aparte); normalizar por prefijo conocido
        if (name.StartsWith("opera", StringComparison.OrdinalIgnoreCase)) return "opera";

        return null;
    }

    private static string? QueryAssocString(AssocStr what, string assoc)
    {
        try
        {
            uint len = 0;
            // Primera llamada: obtener longitud requerida.
            AssocQueryString(AssocF.None, what, assoc, null, null, ref len);
            if (len == 0) return null;

            var sb = new StringBuilder((int)len);
            var hr = AssocQueryString(AssocF.None, what, assoc, null, sb, ref len);
            return hr == 0 ? sb.ToString() : null;
        }
        catch { return null; }
    }

    [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int AssocQueryString(
        AssocF flags, AssocStr str, string pszAssoc, string? pszExtra,
        StringBuilder? pszOut, ref uint pcchOut);

    [Flags]
    private enum AssocF : uint { None = 0 }

    private enum AssocStr
    {
        Executable = 2,
        ProgID = 20,
    }
}
