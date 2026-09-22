using System.Windows.Input;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>
/// Reordenamiento de apps/carpetas sobre <see cref="QuickPanelSettings.Apps"/>. Compartido por
/// Administrar apps, el Dock y el menú Material, para que el drag &amp; drop se comporte igual
/// en los tres lugares. Las carpetas no tienen posición propia: se dibujan en la posición de
/// su primera app, así que mover una carpeta = mover el bloque de sus miembros.
/// No guarda: el llamador decide cuándo persistir (ver <see cref="Commit"/>).
/// </summary>
public static class AppOrdering
{
    private static List<AppEntry> Apps => SettingsService.Current.Apps;

    /// <summary>Suelta una app sobre otra: toma la carpeta de la destino (o queda suelta) y su
    /// posición. Si se arrastra hacia abajo queda DESPUÉS de la destino; hacia arriba, ANTES
    /// (así mover a la vecina siempre produce un cambio visible).</summary>
    public static void MoveApp(AppEntry drag, AppEntry target)
    {
        if (drag.Id == target.Id) return;
        var apps = Apps;
        int from = apps.IndexOf(drag), to = apps.IndexOf(target);
        if (from < 0 || to < 0) return;

        apps.RemoveAt(from);
        int ti = apps.IndexOf(target);
        if (from < to) ti++;                 // hacia abajo: después de la destino
        drag.GroupId = target.GroupId;
        apps.Insert(Math.Clamp(ti, 0, apps.Count), drag);
    }

    /// <summary>Asigna una app a una carpeta y la reubica junto al resto de sus miembros.</summary>
    public static void AssignToGroup(AppEntry app, AppGroup g)
    {
        if (app.GroupId == g.Id) return;
        var apps = Apps;
        apps.Remove(app);
        app.GroupId = g.Id;
        int last = -1;
        for (int i = 0; i < apps.Count; i++) if (apps[i].GroupId == g.Id) last = i;
        if (last >= 0) apps.Insert(last + 1, app); else apps.Add(app);
    }

    public static int FirstMemberIndex(AppGroup g)
    {
        var apps = Apps;
        for (int i = 0; i < apps.Count; i++) if (apps[i].GroupId == g.Id) return i;
        return apps.Count;
    }

    /// <summary>Reubica el bloque completo de miembros de una carpeta ante el índice destino.</summary>
    public static void MoveGroupBlock(AppGroup g, int targetIndex)
    {
        var apps = Apps;
        var members = apps.Where(a => a.GroupId == g.Id).ToList();
        if (members.Count == 0) return;

        // Ancla: la app en la posición destino (si no es del propio grupo), para recalcular
        // el índice tras remover los miembros.
        AppEntry? anchor = (targetIndex >= 0 && targetIndex < apps.Count) ? apps[targetIndex] : null;
        if (anchor != null && anchor.GroupId == g.Id) anchor = null;
        bool movingDown = anchor != null && apps.IndexOf(anchor) > apps.IndexOf(members[0]);

        foreach (var m in members) apps.Remove(m);

        int ins = anchor != null ? apps.IndexOf(anchor) : apps.Count;
        if (ins < 0) ins = apps.Count;
        if (movingDown && anchor != null)
        {
            // Hacia abajo: después del ancla (y de toda su carpeta, si tiene una).
            ins++;
            if (!string.IsNullOrEmpty(anchor.GroupId))
                while (ins < apps.Count && apps[ins].GroupId == anchor.GroupId) ins++;
        }
        apps.InsertRange(Math.Clamp(ins, 0, apps.Count), members);
    }

    /// <summary>
    /// Aplica un drop identificado por claves estables ("app:Id" / "group:Id"), que es como
    /// el Dock y el menú Material etiquetan sus íconos. Devuelve true si cambió algo.
    /// </summary>
    public static bool ApplyDrop(string srcKey, string dstKey)
    {
        if (srcKey == dstKey) return false;
        var s = SettingsService.Current;
        var before = string.Join("|", s.Apps.Select(a => a.Id + ":" + a.GroupId));

        var srcApp   = AppOf(srcKey);
        var srcGroup = GroupOf(srcKey);
        var dstApp   = AppOf(dstKey);
        var dstGroup = GroupOf(dstKey);

        if (srcApp != null)
        {
            if (dstApp != null)        MoveApp(srcApp, dstApp);
            else if (dstGroup != null) AssignToGroup(srcApp, dstGroup);
        }
        else if (srcGroup != null)
        {
            int ti = dstApp != null   ? s.Apps.IndexOf(dstApp)
                   : dstGroup != null ? FirstMemberIndex(dstGroup)
                   : -1;
            // Soltar la carpeta sobre una de sus propias apps: nada que hacer.
            if (ti >= 0 && !(dstApp != null && dstApp.GroupId == srcGroup.Id))
                MoveGroupBlock(srcGroup, ti);
        }

        var after = string.Join("|", s.Apps.Select(a => a.Id + ":" + a.GroupId));
        return before != after;
    }

    /// <summary>Persiste un cambio de orden: reasigna atajos posicionales, guarda y
    /// re-registra hotkeys.</summary>
    public static void Commit()
    {
        ReassignPositionalHotkeys();
        SettingsService.Save();
        App.ReloadHotkeys();
    }

    private static AppEntry? AppOf(string key) =>
        key.StartsWith("app:") ? Apps.FirstOrDefault(a => a.Id == key[4..]) : null;

    private static AppGroup? GroupOf(string key) =>
        key.StartsWith("group:") ? SettingsService.Current.Groups.FirstOrDefault(g => g.Id == key[6..]) : null;

    /// <summary>
    /// Reasigna Ctrl+Alt+[1..0] a las primeras 10 apps según su nueva posición,
    /// pero solo a las que ya tenían un atajo posicional (Ctrl+Alt+dígito).
    /// Los atajos personalizados por el usuario se respetan y no se tocan.
    /// </summary>
    public static void ReassignPositionalHotkeys()
    {
        var apps = Apps;

        bool IsPositional(Hotkey h) =>
            h.IsSet && h.Ctrl && h.Alt && !h.Shift && !h.Win && IsDigit(h.Key);

        foreach (var a in apps)
            if (IsPositional(a.Hotkey)) a.Hotkey = new Hotkey();

        for (int i = 0; i < apps.Count && i < 10; i++)
        {
            if (apps[i].Hotkey.IsSet) continue;
            var key = i < 9 ? Key.D1 + i : Key.D0;
            var hk  = new Hotkey { Ctrl = true, Alt = true, Key = key };
            if (!apps.Any(a => a.Hotkey.IsSet && a.Hotkey.ToString() == hk.ToString()))
                apps[i].Hotkey = hk;
        }
    }

    private static bool IsDigit(Key k) =>
        (k >= Key.D0 && k <= Key.D9) || (k >= Key.NumPad0 && k <= Key.NumPad9);
}
