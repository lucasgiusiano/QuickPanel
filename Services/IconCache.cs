using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>
/// Caché de íconos de apps, en memoria y en disco.
///
/// Antes cada apertura del menú creaba un BitmapImage nuevo con UriSource remoto,
/// disparando una descarga HTTP por ícono cada vez. Eso causaba el retraso visible
/// en el primer despliegue. Aquí se cachea el BitmapImage ya cargado por clave
/// (URL de favicon o ruta de ícono custom); las siguientes aperturas son instantáneas.
///
/// El caché en memoria se respalda en disco (<c>%AppData%\QuickPanel\IconCache</c>),
/// así que la descarga remota ocurre UNA vez en la vida de la app y no en cada arranque.
///
/// Hay dos procedencias posibles para un ícono, y no valen lo mismo:
///
///   .page → el favicon REAL que declara la página, capturado del WebView2 la primera
///           vez que se abre su panel. Es el mismo que el navegador dibuja en la pestaña.
///   .net  → el que devuelve un servicio remoto de favicons (Google, DuckDuckGo, o el
///           /favicon.ico del sitio). Es una aproximación: se usa mientras la app
///           todavía no se abrió nunca.
///
/// El de la página SIEMPRE gana: al leer de disco se prefiere <c>.page</c>, y una vez
/// que una clave tiene ícono de página ninguna descarga remota puede pisarlo (importa
/// porque Preload corre en segundo plano y podría terminar DESPUÉS de que el panel ya
/// capturó el ícono bueno).
///
/// La procedencia vive en el nombre del archivo de caché, no en <see cref="AppEntry"/>,
/// a propósito: el caché es local y NO se sincroniza a la nube, así que un flag dentro
/// del modelo sincronizado diría "tengo ícono de la página" también en la otra PC, donde
/// el archivo no existe.
///
/// Si la primera descarga falla (red lenta, timeout, fuente caída), no se cachea el
/// fallo: se reintenta en segundo plano con fuentes alternativas (DuckDuckGo y el
/// favicon directo del sitio), de modo que el ícono aparezca en la próxima apertura
/// sin congelar la UI.
///
/// Además, Preload() dispara la carga de todos los íconos configurados en segundo
/// plano, para que ya estén listos antes del primer click en el botón.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, BitmapImage> _cache = new();
    private static readonly HashSet<string> _retrying = new();

    /// <summary>Claves cuyo ícono en memoria vino de la página (no se pisan con descargas).</summary>
    private static readonly HashSet<string> _fromPage = new();

    private static readonly object _lock = new();
    private static bool _preloaded;

    /// <summary>Reintentos por fuente ante errores transitorios de red.</summary>
    private const int MaxAttemptsPerSource = 2;

    /// <summary>
    /// Se dispara con la clave del ícono cuando este cambia después de haberse mostrado
    /// (hoy: al capturar el favicon real de la página). Permite que el dock refresque el
    /// ícono ya dibujado sin esperar a la próxima apertura. Puede llegar en cualquier
    /// hilo: quien se suscriba debe marshalear al de UI.
    /// </summary>
    public static event Action<string>? IconUpdated;

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickPanel", "IconCache");

    private const string PageExt = ".page"; // favicon real de la página (prioritario)
    private const string NetExt  = ".net";  // favicon de una fuente remota (aproximado)

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

        // Intento rápido (una sola descarga) para no bloquear el hilo de UI más de lo
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

        // No está en memoria: probar el respaldo en disco (preferencia por .page).
        var (bytes, fromPage) = ReadFromDisk(key);
        if (bytes == null) return null;

        var img = DecodeFrozen(bytes);
        if (img == null) return null;

        lock (_lock)
        {
            // Otro hilo puede haber llegado primero: no pisar un ícono de página.
            if (_cache.TryGetValue(key, out var raced) && _fromPage.Contains(key))
                return raced;

            _cache[key] = img;
            if (fromPage) _fromPage.Add(key);
        }
        return img;
    }

    /// <summary>
    /// Registra el favicon REAL que declara la página, capturado del WebView2 del panel.
    /// Pisa lo que hubiera en memoria y en disco para esa clave, y a partir de acá ninguna
    /// descarga remota vuelve a sobrescribirlo. Notifica vía <see cref="IconUpdated"/>.
    /// </summary>
    public static void SetFromPage(string? key, byte[]? bytes)
    {
        if (string.IsNullOrEmpty(key) || bytes is not { Length: > 0 }) return;

        var img = DecodeFrozen(bytes);
        if (img == null) return; // bytes corruptos o formato no soportado: dejar lo que había

        lock (_lock)
        {
            _cache[key] = img;
            _fromPage.Add(key);
            _retrying.Remove(key); // si había un reintento en vuelo, ya no hace falta
        }

        WriteToDisk(key, bytes, fromPage: true);

        // Fuera del lock: los suscriptores hacen trabajo de UI.
        IconUpdated?.Invoke(key);
    }

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Guarda en memoria y disco un ícono venido de una fuente REMOTA. No hace
    /// nada si esa clave ya tiene el ícono de la página, que tiene prioridad.</summary>
    private static void StoreRemote(string key, BitmapImage img, byte[]? bytes)
    {
        lock (_lock)
        {
            if (_fromPage.Contains(key)) return; // el de la página manda
            _cache[key] = img;
        }
        if (bytes is { Length: > 0 }) WriteToDisk(key, bytes, fromPage: false);
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
            if (img != null) StoreRemote(key, img, bytes);
            lock (_lock) { _retrying.Remove(key); }
        });
    }

    /// <param name="allowFallback">Si es true, recorre fuentes alternativas y reintenta
    /// ante errores transitorios. Si es false, hace una sola descarga de la clave.</param>
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
    private static string CachePath(string key, string ext)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(CacheDir, hash[..32] + ext);
    }

    /// <summary>Lee el ícono respaldado en disco. Prefiere el de la página sobre el remoto.
    /// Devuelve (null, false) si no hay respaldo o no se puede leer.</summary>
    private static (byte[]? bytes, bool fromPage) ReadFromDisk(string key)
    {
        foreach (var (ext, fromPage) in new[] { (PageExt, true), (NetExt, false) })
        {
            try
            {
                var path = CachePath(key, ext);
                if (!File.Exists(path)) continue;
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 0) return (bytes, fromPage);
            }
            catch { /* archivo bloqueado o corrupto: probar la siguiente procedencia */ }
        }
        return (null, false);
    }

    /// <summary>Respalda el ícono en disco. Escribe a un temporal y lo mueve, para que un
    /// lector concurrente nunca vea un archivo a medio escribir.</summary>
    private static void WriteToDisk(string key, byte[] bytes, bool fromPage)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);

            var dest = CachePath(key, fromPage ? PageExt : NetExt);
            var tmp  = dest + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, dest, overwrite: true);

            // Al llegar el ícono real de la página, el aproximado ya no sirve para nada.
            if (fromPage)
            {
                try { File.Delete(CachePath(key, NetExt)); } catch { }
            }
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
