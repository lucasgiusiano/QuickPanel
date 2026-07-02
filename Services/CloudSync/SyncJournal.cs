namespace QuickPanel.Services.CloudSync;

/// <summary>
/// Registro paralelo de metadata de sincronización, separado de los modelos de dominio
/// (AppEntry / AppGroup / Hotkey quedan limpios). Vive dentro de QuickPanelSettings pero
/// en su propia sección serializada.
///
/// - <see cref="ItemModifiedUtc"/>: último cambio por Id (apps, grupos, hotkeys). El merge
///   gana por timestamp más nuevo por Id.
/// - <see cref="Tombstones"/>: Ids borrados recientemente, para que el merge no los resucite.
/// - <see cref="GlobalModifiedUtc"/>: último cambio de la config global sin Id (tema, idioma,
///   tamaños, etc.). Se resuelve por "última que gana" a nivel de todo ese bloque.
/// </summary>
public class SyncJournal
{
    /// <summary>Id de ítem (app/grupo/hotkey) → última modificación UTC.</summary>
    public Dictionary<string, DateTimeOffset> ItemModifiedUtc { get; set; } = new();

    /// <summary>Id borrado → momento del borrado UTC. Se poda por antigüedad al mergear.</summary>
    public Dictionary<string, DateTimeOffset> Tombstones { get; set; } = new();

    /// <summary>Última modificación de la config global sin Id propio.</summary>
    public DateTimeOffset GlobalModifiedUtc { get; set; } = DateTimeOffset.MinValue;

    /// <summary>Los tombstones más viejos que esto se descartan (ya no hay riesgo de resurrección).</summary>
    public static readonly TimeSpan TombstoneTtl = TimeSpan.FromDays(90);

    /// <summary>Marca un ítem como modificado ahora.</summary>
    public void TouchItem(string id, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(id)) return;
        ItemModifiedUtc[id] = now;
        Tombstones.Remove(id); // si estaba borrado y volvió, deja de ser tombstone
    }

    /// <summary>Marca un ítem como borrado ahora (tombstone) y saca su timestamp de vivo.</summary>
    public void KillItem(string id, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(id)) return;
        ItemModifiedUtc.Remove(id);
        Tombstones[id] = now;
    }

    /// <summary>Marca la config global como modificada ahora.</summary>
    public void TouchGlobal(DateTimeOffset now) => GlobalModifiedUtc = now;

    /// <summary>Poda tombstones vencidos (más viejos que el TTL respecto a "now").</summary>
    public void PruneTombstones(DateTimeOffset now)
    {
        var expired = Tombstones.Where(kv => now - kv.Value > TombstoneTtl)
                                .Select(kv => kv.Key).ToList();
        foreach (var id in expired) Tombstones.Remove(id);
    }
}
