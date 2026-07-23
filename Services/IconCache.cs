using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>
/// Caché de favicons de apps, en memoria y en disco.
///
/// Antes cada apertura del menú creaba un BitmapImage nuevo con UriSource remoto,
/// disparando una descarga HTTP por ícono cada vez. Eso causaba el retraso visible
/// en el primer despliegue. Aquí se cachea el BitmapImage ya cargado por clave
/// (URL de favicon o ruta de ícono custom); las siguientes aperturas son instantáneas.
///
/// El caché en memoria se respalda en disco (<c>%AppData%\QuickPanel\IconCache</c>),
/// así que la descarga remota ocurre UNA vez en la vida de la app y no en cada arranque.
///
/// La clave (<see cref="AppEntry.FaviconFor"/>) es el favicon.ico del host EXACTO de la
/// app, sin generalizar a dominio raíz: generalizar rompe apps cuyo ícono vive en el
/// subdominio (mail.google.com no es el logo de Google, es el de Gmail). Si esa URL no
/// responde, <see cref="FaviconSources"/> prueba otras fuentes en orden antes de
/// resignarse a la inicial del nombre en el círculo.
///
/// Antes se probaba además capturar el favicon real desde el WebView2 de cada panel
/// (evento FaviconChanged). Se sacó: la calidad terminaba siendo peor que pedir el
/// favicon.ico directo del host (WebView2 entrega el ícono ya reducido al tamaño de
/// pestaña), y agregaba una segunda procedencia de ícono que había que arbitrar.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, BitmapImage> _cache = new();
    private static readonly HashSet<string> _retrying = new();
    private static readonly object _lock = new();
    private static bool _preloaded;

    /// <summary>Reintentos por fuente ante errores transitorios de red.</summary>
    private const int MaxAttemptsPerSource = 2;

    /// <summary>
    /// Se dispara con la clave del ícono cuando termina de descargarse en segundo plano
    /// (el intento rápido inicial había fallado). Permite que un dock/panel ya abierto
    /// reemplace la inicial de respaldo sin esperar a la próxima apertura. Puede llegar
    /// en cualquier hilo: quien se suscriba debe marshalear al de UI.
    /// </summary>
    public static event Action<string>? IconUpdated;

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickPanel", "IconCache");

    private const string CacheExt = ".ico";

    /// <summary>Devuelve la clave de ícono de una app (ruta custom o URL de favicon).</summary>
    public static string KeyFor(AppEntry app) =>
        app.HasCustomIcon ? app.IconPath : AppEntry.FaviconFor(app.Url);

    /// <summary>Obtiene el BitmapImage cacheado para una clave, cargándolo si hace falta.
    /// Devuelve null si la clave es vacía o la carga falla. Ante un fallo de descarga,
    /// dispara un reintento en segundo plano con fuentes alternativas.</summary>
    public static BitmapImage? Get(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        var cached = TryGetCached(key);
        if (cached != null) return cached;

        // Intento rápido (una sola fuente) para no bloquear el hilo de UI más de lo
        // estrictamente necesario.
        var img = Load(key, allowFallback: false, out var bytes);
        if (img != null)
        {
            StoreRemote(key, img, bytes);
            return img;
        }

        // Falló el intento rápido: reintentar en segundo plano con fuentes alternativas.
        RetryInBackground(key);
        return null;
    }

    /// <summary>Busca la clave en memoria y luego en el caché de disco, SIN tocar la red.
    /// Para llamar desde el hilo de UI cuando no se quiere arriesgar un bloqueo.</summary>
    public static BitmapImage? TryGetCached(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        var bytes = ReadFromDisk(key);
        if (bytes == null) return null;

        var img = DecodeFrozen(bytes);
        if (img == null) return null;

        lock (_lock) { _cache[key] = img; }
        return img;
    }

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Guarda en memoria y disco un ícono descargado.</summary>
    private static void StoreRemote(string key, BitmapImage img, byte[]? bytes)
    {
        lock (_lock) { _cache[key] = img; }
        if (bytes is { Length: > 0 }) WriteToDisk(key, bytes);
    }

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
            var img = Load(key, allowFallback: true, out var bytes);
            if (img != null)
            {
                StoreRemote(key, img, bytes);
                IconUpdated?.Invoke(key); // el panel ya podría estar mostrando la inicial de respaldo
            }
            lock (_lock) { _retrying.Remove(key); }
        });
    }

    /// <param name="allowFallback">Si es true, recorre fuentes alternativas y reintenta
    /// ante errores transitorios. Si es false, prueba una sola fuente.</param>
    /// <param name="raw">Bytes crudos de la imagen, para poder respaldarla en disco.</param>
    private static BitmapImage? Load(string key, bool allowFallback, out byte[]? raw)
    {
        raw = null;
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

            var img = DecodeFrozen(bytes);
            if (img != null) raw = bytes;
            return img;
        }
        catch { return null; }
    }

    // ── Respaldo en disco ──

    /// <summary>Ruta del archivo de caché para una clave. La clave puede ser una URL o
    /// una ruta de archivo, así que se hashea para obtener un nombre válido y estable.</summary>
    private static string CachePath(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(CacheDir, hash[..32] + CacheExt);
    }

    /// <summary>Lee el ícono respaldado en disco. Devuelve null si no hay respaldo o no
    /// se puede leer.</summary>
    private static byte[]? ReadFromDisk(string key)
    {
        try
        {
            var path = CachePath(key);
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            return bytes.Length > 0 ? bytes : null;
        }
        catch { return null; } // archivo bloqueado o corrupto
    }

    /// <summary>Respalda el ícono en disco. Escribe a un temporal y lo mueve, para que un
    /// lector concurrente nunca vea un archivo a medio escribir.</summary>
    private static void WriteToDisk(string key, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);

            var dest = CachePath(key);
            var tmp  = dest + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, dest, overwrite: true);
        }
        catch { /* el caché de disco es una optimización: si falla, se sigue con memoria */ }
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

    /// <summary>
    /// Fuentes de favicon a probar en orden, de más a menos específica:
    ///
    ///   1. favicon.ico del host EXACTO (la clave misma) — el que declara la propia app.
    ///      Es la fuente más confiable cuando existe: es el archivo real del sitio.
    ///   2. DuckDuckGo sobre el host exacto — conoce el ícono de sitios que no sirven
    ///      favicon.ico en la raíz (ej. subdominios de Google como gemini.google.com o
    ///      home.google.com). A diferencia de Google Favicons, responde 404 cuando no
    ///      tiene nada, así que no tapa un mejor resultado más adelante en la cadena.
    ///   3 y 4. Lo mismo, pero sobre el dominio raíz (sin subdominio): red de seguridad
    ///      para hosts cuyo ícono en realidad vive en el dominio padre.
    ///   5. Google s2favicons sobre el dominio raíz, al final: casi siempre devuelve algo
    ///      (200) aunque sea un ícono genérico — mejor eso que la inicial del nombre.
    /// </summary>
    private static IEnumerable<string> FaviconSources(string primaryUrl)
    {
        yield return primaryUrl;

        string host;
        try { host = new Uri(primaryUrl).Host; }
        catch { yield break; }
        if (string.IsNullOrEmpty(host)) yield break;

        yield return $"https://icons.duckduckgo.com/ip3/{host}.ico";

        var root = RootDomain(host);
        if (root != host)
        {
            yield return $"https://{root}/favicon.ico";
            yield return $"https://icons.duckduckgo.com/ip3/{root}.ico";
        }

        yield return $"https://www.google.com/s2/favicons?sz=128&domain={root}";
    }

    /// <summary>Aproxima el dominio raíz quedándose con las últimas dos etiquetas
    /// (ej. mail.google.com -> google.com). No es exacto para TLDs compuestos
    /// (co.uk, com.ar…), pero acá solo se usa como red de seguridad final.</summary>
    private static string RootDomain(string host)
    {
        var parts = host.Split('.');
        return parts.Length > 2 ? string.Join('.', parts[^2..]) : host;
    }

    private static bool IsHttp(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Precarga en segundo plano los íconos de todas las apps configuradas.
    /// Se llama una sola vez, cuando ya hay un navegador compatible detectado.
    /// Con el respaldo en disco, en el segundo arranque esto no toca la red.</summary>
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

                // Memoria o disco primero: evita salir a la red por algo ya respaldado.
                if (TryGetCached(key) != null) continue;

                // En la precarga usamos la cadena completa de fuentes y reintentos.
                var img = Load(key, allowFallback: true, out var bytes);
                if (img != null) StoreRemote(key, img, bytes);
            }
        });
    }
}
