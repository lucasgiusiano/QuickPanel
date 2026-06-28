using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>
/// Caché en memoria de íconos de apps (favicons remotos y archivos locales).
///
/// Antes cada apertura del menú creaba un BitmapImage nuevo con UriSource remoto,
/// disparando una descarga HTTP por ícono cada vez. Eso causaba el retraso visible
/// en el primer despliegue. Aquí se cachea el BitmapImage ya cargado por clave
/// (URL de favicon o ruta de ícono custom); las siguientes aperturas son instantáneas.
///
/// Si la primera descarga falla (red lenta, timeout, fuente caída), no se cachea el
/// fallo: se reintenta en segundo plano con fuentes alternativas (DuckDuckGo y el
/// favicon directo del sitio), de modo que el ícono aparezca en la próxima apertura
/// sin congelar la UI.
///
/// Además, Preload() dispara la descarga de todos los íconos configurados en
/// segundo plano, para que ya estén listos antes del primer click en el botón.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, BitmapImage> _cache = new();
    private static readonly HashSet<string> _retrying = new();
    private static readonly object _lock = new();
    private static bool _preloaded;

    /// <summary>Reintentos por fuente ante errores transitorios de red.</summary>
    private const int MaxAttemptsPerSource = 2;

    /// <summary>Devuelve la clave de ícono de una app (ruta custom o URL de favicon).</summary>
    public static string KeyFor(AppEntry app) =>
        app.HasCustomIcon ? app.IconPath : AppEntry.FaviconFor(app.Url);

    /// <summary>Obtiene el BitmapImage cacheado para una clave, cargándolo si hace falta.
    /// Devuelve null si la clave es vacía o la carga falla. Ante un fallo de descarga,
    /// dispara un reintento en segundo plano con fuentes alternativas.</summary>
    public static BitmapImage? Get(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        // Intento rápido (una sola descarga) para no bloquear el hilo de UI más de lo
        // estrictamente necesario.
        var img = Load(key, allowFallback: false);
        if (img != null)
        {
            lock (_lock) { _cache[key] = img; }
            return img;
        }

        // Falló el intento rápido: reintentar en segundo plano con fuentes alternativas.
        RetryInBackground(key);
        return null;
    }

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Lanza (una sola vez por clave) un reintento en segundo plano con la
    /// cadena completa de fuentes y reintentos. No bloquea al llamador.</summary>
    private static void RetryInBackground(string key)
    {
        lock (_lock)
        {
            if (_cache.ContainsKey(key)) return;
            if (!_retrying.Add(key)) return; // ya hay un reintento en curso para esta clave
        }

        Task.Run(() =>
        {
            var img = Load(key, allowFallback: true);
            lock (_lock)
            {
                if (img != null) _cache[key] = img;
                _retrying.Remove(key);
            }
        });
    }

    /// <param name="allowFallback">Si es true, recorre fuentes alternativas y reintenta
    /// ante errores transitorios. Si es false, hace una sola descarga de la clave.</param>
    private static BitmapImage? Load(string key, bool allowFallback)
    {
        try
        {
            byte[]? bytes;

            // Para URIs remotos (favicons), descargar los bytes de forma síncrona en
            // este hilo. Crear un BitmapImage con UriSource remoto descarga ASÍNCRONO
            // y la imagen no está lista al retornar (ni se puede Freeze), por eso antes
            // se cacheaban imágenes vacías. Cargando los bytes a un stream, la imagen
            // queda completa y congelable.
            if (IsHttp(key))
                bytes = allowFallback ? DownloadWithFallback(key) : DownloadOnce(key);
            else
                bytes = File.ReadAllBytes(key); // ruta de archivo local (ícono custom)

            if (bytes is not { Length: > 0 }) return null;
            return DecodeFrozen(bytes);
        }
        catch { return null; }
    }

    /// <summary>Decodifica bytes a un BitmapImage congelado (usable desde cualquier hilo).
    /// Devuelve null si los bytes no son una imagen válida.</summary>
    private static BitmapImage? DecodeFrozen(byte[] bytes)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(bytes);
            img.CacheOption  = BitmapCacheOption.OnLoad; // copia el stream al decodificar
            img.EndInit();
            if (img.CanFreeze) img.Freeze();
            return img;
        }
        catch { return null; }
    }

    /// <summary>Una sola descarga, sin reintentos ni fuentes alternativas.</summary>
    private static byte[]? DownloadOnce(string url)
    {
        try
        {
            var bytes = _http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            return bytes is { Length: > 0 } ? bytes : null;
        }
        catch { return null; }
    }

    /// <summary>Recorre las fuentes de favicon en orden, reintentando cada una ante
    /// errores transitorios. Devuelve los bytes de la primera que responda, o null.</summary>
    private static byte[]? DownloadWithFallback(string primaryUrl)
    {
        foreach (var url in FaviconSources(primaryUrl))
        {
            for (int attempt = 1; attempt <= MaxAttemptsPerSource; attempt++)
            {
                try
                {
                    var bytes = _http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                    if (bytes is { Length: > 0 }) return bytes;
                }
                catch
                {
                    if (attempt < MaxAttemptsPerSource)
                        Thread.Sleep(200 * attempt); // backoff corto antes de reintentar
                }
            }
            // esta fuente agotó sus reintentos → probar la siguiente
        }
        return null;
    }

    /// <summary>Fuentes de favicon a probar en orden: la original (Google), y luego
    /// alternativas derivadas del host para tolerar que una fuente esté caída/bloqueada.</summary>
    private static IEnumerable<string> FaviconSources(string primaryUrl)
    {
        yield return primaryUrl;

        var host = HostFromFaviconUrl(primaryUrl);
        if (!string.IsNullOrEmpty(host))
        {
            yield return $"https://icons.duckduckgo.com/ip3/{host}.ico";
            yield return $"https://{host}/favicon.ico";
        }
    }

    /// <summary>Extrae el host del parámetro <c>domain=</c> de una URL de Google favicons;
    /// si no lo encuentra, usa el host de la propia URL.</summary>
    private static string HostFromFaviconUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            foreach (var part in uri.Query.TrimStart('?').Split('&'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("domain", StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(kv[1]);
            }
            return uri.Host;
        }
        catch { return ""; }
    }

    private static bool IsHttp(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Precarga en segundo plano los íconos de todas las apps configuradas.
    /// Se llama una sola vez, cuando ya hay un navegador compatible detectado.</summary>
    public static void Preload()
    {
        if (_preloaded) return;
        _preloaded = true;

        var apps = SettingsService.Current.Apps.ToList();
        Task.Run(() =>
        {
            foreach (var app in apps)
            {
                var key = KeyFor(app);
                if (string.IsNullOrEmpty(key)) continue;
                lock (_lock)
                {
                    if (_cache.ContainsKey(key)) continue;
                }
                // En la precarga usamos la cadena completa de fuentes y reintentos.
                var img = Load(key, allowFallback: true);
                if (img != null)
                {
                    lock (_lock) { _cache[key] = img; }
                }
            }
        });
    }
}
