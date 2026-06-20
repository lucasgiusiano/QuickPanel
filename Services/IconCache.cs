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
/// Además, Preload() dispara la descarga de todos los íconos configurados en
/// segundo plano, para que ya estén listos antes del primer click en el botón.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, BitmapImage> _cache = new();
    private static readonly object _lock = new();
    private static bool _preloaded;

    /// <summary>Devuelve la clave de ícono de una app (ruta custom o URL de favicon).</summary>
    public static string KeyFor(AppEntry app) =>
        app.HasCustomIcon ? app.IconPath : AppEntry.FaviconFor(app.Url);

    /// <summary>Obtiene el BitmapImage cacheado para una clave, cargándolo si hace falta.
    /// Devuelve null si la clave es vacía o la carga falla.</summary>
    public static BitmapImage? Get(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        var img = Load(key);
        if (img != null)
        {
            lock (_lock) { _cache[key] = img; }
        }
        return img;
    }

    private static BitmapImage? Load(string key)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource   = new Uri(key);
            img.CacheOption = BitmapCacheOption.OnLoad; // descarga completa al cargar
            img.EndInit();
            // Congelar permite usarlo desde cualquier hilo y evita recargas.
            if (img.CanFreeze) img.Freeze();
            return img;
        }
        catch { return null; }
    }

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
                var img = Load(key);
                if (img != null)
                {
                    lock (_lock) { _cache[key] = img; }
                }
            }
        });
    }
}
