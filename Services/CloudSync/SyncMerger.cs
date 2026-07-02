using QuickPanel.Models;

namespace QuickPanel.Services.CloudSync;

/// <summary>Resultado del merge, incluyendo si quedaron conflictos ambiguos para resolver.</summary>
public sealed record MergeResult(QuickPanelSettings Merged, List<MergeConflict> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}

/// <summary>Conflicto ambiguo: mismo Id editado distinto en ambos lados con timestamps empatados.</summary>
public sealed record MergeConflict(string ItemKind, string Id, string LocalLabel, string RemoteLabel);

/// <summary>
/// Merge por campo entre la config local y la remota (bajada de la nube), usando el
/// <see cref="SyncJournal"/> de cada lado. Determinístico y sin efectos secundarios:
/// no toca disco ni red, por eso es testeable en aislamiento.
///
/// Reglas (del diseño acordado):
/// - Colecciones con Id (Apps, Groups, ActionHotkeys): gana el ítem con ModifiedUtc más
///   nuevo por Id; los Ids que solo existen de un lado se agregan; los tombstones evitan
///   resucitar lo borrado del otro lado. Empate real de timestamp con contenido distinto
///   → se reporta como conflicto (el llamador decide; por defecto se mantiene el local).
/// - Config global sin Id (tema, idioma, tamaños...): gana el bloque con GlobalModifiedUtc
///   más nuevo, entero (no se parte campo por campo).
/// - CloudProvider / CloudAccount / SyncInterval NO se sincronizan: son locales de cada PC.
/// </summary>
public static class SyncMerger
{
    public static MergeResult Merge(QuickPanelSettings local, QuickPanelSettings remote, DateTimeOffset now)
    {
        var conflicts = new List<MergeConflict>();
        var lj = local.SyncJournal;
        var rj = remote.SyncJournal;

        var result = new QuickPanelSettings();

        // ── 1) Config global: última que gana, bloque entero ──────────
        var globalSource = rj.GlobalModifiedUtc > lj.GlobalModifiedUtc ? remote : local;
        CopyGlobalScalars(globalSource, result);

        // ── 2) Colecciones por Id ─────────────────────────────────────
        result.Apps = MergeById(
            local.Apps, remote.Apps, a => a.Id, lj, rj, now, conflicts,
            "app", a => a.Name);

        result.Groups = MergeById(
            local.Groups, remote.Groups, g => g.Id, lj, rj, now, conflicts,
            "group", g => g.Name);

        result.ActionHotkeys = MergeHotkeys(local.ActionHotkeys, remote.ActionHotkeys, lj, rj, now);

        // ── 3) Journal mergeado ───────────────────────────────────────
        result.SyncJournal = MergeJournal(lj, rj, result, now);

        // ── 4) Campos locales (no se sincronizan): preservar los del local ─
        result.CloudProvider = local.CloudProvider;
        result.CloudAccount  = local.CloudAccount;
        result.SyncInterval  = local.SyncInterval;

        return new MergeResult(result, conflicts);
    }

    /// <summary>Copia los campos globales sin Id desde la fuente ganadora.</summary>
    private static void CopyGlobalScalars(QuickPanelSettings src, QuickPanelSettings dst)
    {
        dst.SeedColor    = src.SeedColor;
        dst.ThemeMode    = src.ThemeMode;
        dst.PanelWidth   = src.PanelWidth;
        dst.PanelHeight  = src.PanelHeight;
        dst.MenuItemSize = src.MenuItemSize;
        dst.MenuMode     = src.MenuMode;
        dst.Language     = src.Language;
        dst.ButtonRelX   = src.ButtonRelX;
        dst.ButtonRelY   = src.ButtonRelY;
        dst.RunAtStartup = src.RunAtStartup;
        dst.StartAppId   = src.StartAppId;
        dst.AutoHide     = src.AutoHide;
        dst.ShowBadges   = src.ShowBadges;
        dst.LiteMode     = src.LiteMode;
    }

    private static List<T> MergeById<T>(
        List<T> local, List<T> remote, Func<T, string> idOf,
        SyncJournal lj, SyncJournal rj, DateTimeOffset now,
        List<MergeConflict> conflicts, string kind, Func<T, string> labelOf)
    {
        var localById  = local.ToDictionary(idOf);
        var remoteById = remote.ToDictionary(idOf);
        var allIds = localById.Keys.Union(remoteById.Keys).ToHashSet();
        var merged = new List<T>();

        foreach (var id in allIds)
        {
            bool inLocal  = localById.TryGetValue(id, out var lItem);
            bool inRemote = remoteById.TryGetValue(id, out var rItem);

            // Tombstone gana sobre "vivo" si el borrado es más nuevo que el último cambio del otro lado.
            var lDeleted = lj.Tombstones.TryGetValue(id, out var lDel) ? (DateTimeOffset?)lDel : null;
            var rDeleted = rj.Tombstones.TryGetValue(id, out var rDel) ? (DateTimeOffset?)rDel : null;
            var lMod = lj.ItemModifiedUtc.TryGetValue(id, out var lm) ? (DateTimeOffset?)lm : null;
            var rMod = rj.ItemModifiedUtc.TryGetValue(id, out var rm) ? (DateTimeOffset?)rm : null;

            // ¿Borrado en algún lado vence al cambio del otro?
            if (Deleted(lDeleted, rMod) || Deleted(rDeleted, lMod))
                continue; // permanece borrado

            if (inLocal && !inRemote) { merged.Add(lItem!); continue; }
            if (!inLocal && inRemote) { merged.Add(rItem!); continue; }

            // Existe en ambos: gana el timestamp más nuevo.
            var lt = lMod ?? DateTimeOffset.MinValue;
            var rt = rMod ?? DateTimeOffset.MinValue;

            if (rt > lt)      merged.Add(rItem!);
            else if (lt > rt) merged.Add(lItem!);
            else
            {
                // Empate: si el contenido difiere, es conflicto ambiguo → mantener local, reportar.
                merged.Add(lItem!);
                if (!Equals(labelOf(lItem!), labelOf(rItem!)))
                    conflicts.Add(new MergeConflict(kind, id, labelOf(lItem!), labelOf(rItem!)));
            }
        }

        return merged;
    }

    /// <summary>True si un borrado (deletedAt) vence al último cambio del otro lado (otherMod).</summary>
    private static bool Deleted(DateTimeOffset? deletedAt, DateTimeOffset? otherMod)
    {
        if (deletedAt is null) return false;
        if (otherMod is null) return true;            // borrado de un lado, el otro nunca lo tocó
        return deletedAt.Value >= otherMod.Value;      // borrado más nuevo o igual que el cambio ajeno
    }

    private static Dictionary<string, Hotkey> MergeHotkeys(
        Dictionary<string, Hotkey> local, Dictionary<string, Hotkey> remote,
        SyncJournal lj, SyncJournal rj, DateTimeOffset now)
    {
        var merged = new Dictionary<string, Hotkey>();
        var allKeys = local.Keys.Union(remote.Keys).ToHashSet();

        foreach (var key in allKeys)
        {
            bool inLocal  = local.TryGetValue(key, out var lHk);
            bool inRemote = remote.TryGetValue(key, out var rHk);

            var lMod = lj.ItemModifiedUtc.TryGetValue(key, out var lm) ? (DateTimeOffset?)lm : null;
            var rMod = rj.ItemModifiedUtc.TryGetValue(key, out var rm) ? (DateTimeOffset?)rm : null;
            var lDel = lj.Tombstones.TryGetValue(key, out var ld) ? (DateTimeOffset?)ld : null;
            var rDel = rj.Tombstones.TryGetValue(key, out var rd) ? (DateTimeOffset?)rd : null;

            if (Deleted(lDel, rMod) || Deleted(rDel, lMod)) continue;
            if (inLocal && !inRemote) { merged[key] = lHk!; continue; }
            if (!inLocal && inRemote) { merged[key] = rHk!; continue; }

            var lt = lMod ?? DateTimeOffset.MinValue;
            var rt = rMod ?? DateTimeOffset.MinValue;
            merged[key] = rt > lt ? rHk! : lHk!;
        }

        return merged;
    }

    /// <summary>
    /// Combina los journals de ambos lados hacia el resultado: por cada Id vivo o borrado,
    /// gana el timestamp más reciente. Global toma el máximo.
    /// </summary>
    private static SyncJournal MergeJournal(SyncJournal lj, SyncJournal rj, QuickPanelSettings merged, DateTimeOffset now)
    {
        var j = new SyncJournal
        {
            GlobalModifiedUtc = lj.GlobalModifiedUtc > rj.GlobalModifiedUtc
                ? lj.GlobalModifiedUtc : rj.GlobalModifiedUtc
        };

        // Ids vivos en el resultado.
        var liveIds = merged.Apps.Select(a => a.Id)
            .Concat(merged.Groups.Select(g => g.Id))
            .Concat(merged.ActionHotkeys.Keys)
            .ToHashSet();

        foreach (var id in liveIds)
        {
            var lm = lj.ItemModifiedUtc.TryGetValue(id, out var l) ? (DateTimeOffset?)l : null;
            var rm = rj.ItemModifiedUtc.TryGetValue(id, out var r) ? (DateTimeOffset?)r : null;
            var newest = Max(lm, rm) ?? now;
            j.ItemModifiedUtc[id] = newest;
        }

        // Tombstones: unión, gana el borrado más nuevo. Se descartan los que quedaron vivos.
        foreach (var kv in lj.Tombstones.Concat(rj.Tombstones))
        {
            if (liveIds.Contains(kv.Key)) continue;
            if (!j.Tombstones.TryGetValue(kv.Key, out var existing) || kv.Value > existing)
                j.Tombstones[kv.Key] = kv.Value;
        }

        j.PruneTombstones(now);
        return j;
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.Value > b.Value ? a : b;
    }
}
