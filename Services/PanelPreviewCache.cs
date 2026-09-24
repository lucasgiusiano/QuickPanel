using System.Windows.Media.Imaging;

namespace QuickPanel.Services;

/// <summary>
/// Última captura del contenido de cada app (clave = Id de la app), para la animación de
/// apertura: el panel se despliega mostrando cómo se veía la app, y recién al terminar se
/// cambia por el panel real, que para entonces ya cargó (o se actualiza solo).
///
/// Es global y no por ventana: al abrir WhatsApp por primera vez en una ventana nueva del
/// navegador (o en otro monitor) se ve la captura tomada en otra, en vez de un genérico.
/// Solo en memoria: al reiniciar la app, la primera apertura usa el placeholder.
/// </summary>
public static class PanelPreviewCache
{
    private static readonly Dictionary<string, BitmapSource> _previews = new();

    public static bool TryGet(string appId, out BitmapSource? preview)
    {
        bool ok = _previews.TryGetValue(appId, out var p);
        preview = p;
        return ok;
    }

    /// <summary>Guarda una captura. Debe venir congelada (Freeze) para poder compartirse.</summary>
    public static void Set(string appId, BitmapSource preview) => _previews[appId] = preview;

    /// <summary>Descarta la captura (la app cambió de URL o se quitó).</summary>
    public static void Remove(string appId) => _previews.Remove(appId);
}
