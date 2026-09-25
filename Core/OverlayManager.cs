using System.Linq;
using System.Windows;
using QuickPanel.AppWindow;
using QuickPanel.Models;
using QuickPanel.Overlay;
using QuickPanel.Services;
using QuickPanel.Settings;

namespace QuickPanel.Core;

/// <summary>
/// Una instancia por ancla: cada ventana del navegador (modo clásico) o cada monitor
/// (modo escritorio). Maneja el dock o el botón flotante, el menú radial y los paneles.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    /// <summary>A qué está anclado este overlay (ventana del navegador o monitor).</summary>
    public OverlayAnchor Anchor { get; }

    /// <summary>Ventana del navegador dueña (Zero en modo escritorio).</summary>
    public IntPtr EdgeHwnd => Anchor.OwnerHwnd;

    private readonly FloatingButtonWindow? _button;
    private readonly DockBarWindow? _dock;
    private readonly bool _dockMode;
    private MenuWindow? _menu;
    private readonly Dictionary<string, AppHostWindow> _appWindows = new();
    private bool _disposed;

    /// <summary>Id de la carpeta actualmente expandida en el menú (una sola a la vez).
    /// Vive mientras esta ventana de Edge esté abierta, así el menú la recuerda entre
    /// aperturas. Null = ninguna expandida.</summary>
    public string? ExpandedGroupId { get; set; }

    private const double ButtonSizeDip = 56;

    private static bool _startAppLaunched;

    public OverlayManager(OverlayAnchor anchor)
    {
        Anchor = anchor;
        _dockMode = SettingsService.Current.MenuMode == MenuMode.Dock;

        if (_dockMode)
        {
            _dock = new DockBarWindow(this);
            _dock.SetEdgeOwner(anchor.OwnerHwnd);   // Zero en escritorio: sin dueño, topmost
            _dock.Show();
        }
        else
        {
            _button = new FloatingButtonWindow(this);
            // El owner nativo (Edge) se asigna dentro del botón en SourceInitialized.
            _button.SetEdgeOwner(anchor.OwnerHwnd);
            _button.Show();
        }

        // Precrear la ventana de la animación de apertura en segundo plano, para que el
        // primer clic no pague el costo de crear una ventana transparente.
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed && SettingsService.Current.PanelAnimation != PanelAnimation.Off)
                _ = Proxy;
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        Reposition();
        TryLaunchStartApp();
    }

    /// <summary>Abre la app configurada como "inicio" una sola vez por sesión,
    /// en la primera ventana de Edge detectada (Pro).</summary>
    private void TryLaunchStartApp()
    {
        if (_startAppLaunched) return;
        var id = SettingsService.Current.StartAppId;
        if (string.IsNullOrEmpty(id)) return;

        var app = SettingsService.Current.Apps.FirstOrDefault(a => a.Id == id);
        if (app == null) return;

        _startAppLaunched = true;
        // Diferido: el botón/overlay recién se está montando.
        var disp = (System.Windows.Window?)_button ?? _dock;
        disp?.Dispatcher.BeginInvoke(new Action(() => OpenApp(app, 0.5)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    // ── Posicionamiento ──

    public void Reposition()
    {
        if (_disposed || !Anchor.IsAlive) return;

        if (Anchor.IsMinimized)
        {
            CloseMenu();
            return; // owned window: Windows ya la oculta junto al owner
        }

        UpdateFullscreen();

        if (_dockMode)
        {
            _dock!.Reanchor();
            foreach (var w in _appWindows.Values)
            {
                try { w.Reanchor(); } catch { }
            }
            return;
        }

        var b = Anchor.BoundsDip;
        var (rx, ry) = Anchor.ButtonRel;
        _button!.Left = b.Left + rx * Math.Max(0, b.Width - ButtonSizeDip);
        _button!.Top  = b.Top  + ry * Math.Max(0, b.Height - ButtonSizeDip);

        // Re-anclar paneles abiertos al nuevo rect de Edge
        foreach (var w in _appWindows.Values)
        {
            try { w.Reanchor(); } catch { }
        }

        CloseMenu(); // si Edge se mueve, el menú queda desfasado: se cierra
    }

    private bool _lastFullscreen;

    /// <summary>Pantalla completa (video/F11) sobre el ancla: ocultar pestaña del dock o botón.</summary>
    private void UpdateFullscreen()
    {
        bool fullscreen = Anchor.IsFullscreen;
        _dock?.SetFullscreen(fullscreen);
        _button?.SetFullscreen(fullscreen);
        if (fullscreen && !_lastFullscreen) CloseMenu();
        _lastFullscreen = fullscreen;
    }

    private PanelGeometry.Rect _lastBounds;

    /// <summary>
    /// Tick periódico del modo escritorio (no hay ventana de navegador que avise con
    /// LOCATIONCHANGE): detecta pantalla completa de la app en primer plano y cambios del
    /// área de trabajo (barra de tareas movida/redimensionada, resolución).
    /// </summary>
    public void DesktopTick()
    {
        if (_disposed || !Anchor.IsAlive) return;
        UpdateFullscreen();
        var b = Anchor.BoundsDip;
        if (b != _lastBounds)
        {
            _lastBounds = b;
            Reposition();
        }
    }

    /// <summary>Re-ancla los paneles de app abiertos (sin reposicionar el botón
    /// ni cerrar el menú). Usado tras cambiar el tamaño del panel en caliente.</summary>
    public void ReanchorOpenPanels()
    {
        foreach (var w in _appWindows.Values)
        {
            try { w.Reanchor(); } catch { }
        }
    }

    /// <summary>Guarda la posición actual del botón como fracción del rect de referencia
    /// (ventana del navegador, o monitor en modo escritorio — uno por monitor).</summary>
    public void SaveButtonPositionFromCurrent()
    {
        if (_dockMode || _button == null) return;

        var b = Anchor.BoundsDip;
        Anchor.ButtonRel = (
            Math.Clamp((_button.Left - b.Left) / Math.Max(1, b.Width - ButtonSizeDip), 0, 1),
            Math.Clamp((_button.Top  - b.Top)  / Math.Max(1, b.Height - ButtonSizeDip), 0, 1));
        SettingsService.Save();
    }

    /// <summary>Lado hacia el que se despliegan los paneles del botón flotante: el opuesto a
    /// la mitad de la referencia en la que está el botón.</summary>
    public PanelSide ButtonSide => PanelGeometry.SideFor(Anchor.ButtonRel.X);

    // ── Menú ──

    public void ToggleMenu()
    {
        if (_dockMode || _button == null) return; // el dock no usa menú radial

        if (IsMenuOpen) { CloseMenu(); return; }

        CloseMenu();
        _menu = new MenuWindow(this, _button);
        _menuOpen = true;
        // El menú puede cerrarse solo (perder foco, Escape) sin pasar por CloseMenu;
        // el evento Closed garantiza que el flag se resetee en cualquier caso.
        _menu.Closed += (_, _) => _menuOpen = false;
        _menu.Show();
    }

    private bool _menuOpen;

    /// <summary>True si el menú de apps está desplegado.</summary>
    public bool IsMenuOpen => _menuOpen;

    public void CloseMenu()
    {
        _menuOpen = false;
        if (_menu != null)
        {
            try { _menu.Close(); } catch { }
            _menu = null;
        }
    }

    // ── Apps ──

    /// <summary>
    /// Dock horizontal: centro del ícono de cada app medido desde el inicio de la barra.
    /// Relativo (no absoluto) para que el panel siga al ícono si la ventana se mueve.
    /// </summary>
    private readonly Dictionary<string, double> _iconOffsets = new();

    /// <summary>Abre una app. <paramref name="iconRectDip"/> es el rect del ícono que la abrió
    /// (dock horizontal: el panel se alinea con él; animación: crece desde ahí); null = centro
    /// de la barra.</summary>
    public void OpenApp(AppEntry app, double originRelY, PanelGeometry.Rect? iconRectDip = null)
    {
        CloseMenu();

        // Una apertura nueva interrumpe la animación en curso (si la hay).
        _revealTicket++;
        _proxy?.HideNow();
        var anim = SettingsService.Current.PanelAnimation;

        if (_dockMode && _dock!.IsHorizontal)
        {
            var bar = _dock.BarRect();
            _iconOffsets[app.Id] = iconRectDip is { } ir
                ? ir.CenterX - bar.Left
                : _iconOffsets.TryGetValue(app.Id, out var prev) ? prev : bar.Width / 2;
        }

        // Un solo panel visible a la vez: ocultar los demás (sin destruirlos,
        // así conservan su caché/sesión de WebView2).
        foreach (var kv in _appWindows)
            if (kv.Key != app.Id && kv.Value.IsVisible)
                kv.Value.HideFromHotkey();

        _appWindows.TryGetValue(app.Id, out var existing);
        if (existing != null)
        {
            // Recalcular el lado por si el botón se movió desde la última apertura.
            existing.UpdateSide(CurrentSideFor(app.Id));
            TouchLru(app.Id);
        }

        // Sin animación, o ya visible (nada que desplegar): comportamiento clásico.
        if (anim == PanelAnimation.Off || existing?.IsShownToUser == true)
        {
            if (existing != null) existing.ShowAndFocus();
            else CreatePanel(app, originRelY, cloaked: false).Show();
            return;
        }

        _ = RevealAsync(app, existing, originRelY, anim, iconRectDip);
    }

    /// <summary>Crea el panel de una app y lo registra (sin mostrarlo).</summary>
    private AppHostWindow CreatePanel(AppEntry app, double originRelY, bool cloaked)
    {
        // Modo Lite: tope de paneles vivos. Antes de crear uno nuevo, si ya se
        // alcanzó el máximo, matar (ForceClose) el menos usado recientemente.
        EnforceLiveLimit();

        // Callbacks: capturan el rect ACTUAL del botón/barra y el lado ACTUAL en cada
        // llamada, así el panel se re-ancla bien aunque Edge/el botón se muevan.
        string id = app.Id;
        var win = new AppHostWindow(
            app, Anchor.OwnerHwnd, CurrentSideFor(id), originRelY,
            computeBounds: w => PanelGeometry.Compute(Anchor.BoundsDip, Placement, CurrentSideFor(id), w, AnchorRectFor(id)),
            maxWidth: () => PanelGeometry.MaxWidth(Anchor.BoundsDip, Placement, CurrentSideFor(id), AnchorRectFor(id)),
            isAnchorAlive: () => Anchor.IsAlive,
            // Con animación, el panel arranca encubierto: carga por detrás mientras
            // la animación corre encima.
            startCloaked: cloaked);

        win.Closed += (_, _) =>
        {
            _appWindows.Remove(app.Id);
            _lru.Remove(app.Id);
            _unread.Remove(app.Id);
            NotifyUnread();
        };
        win.UnreadChanged += w =>
        {
            _unread[w.AppId] = w.Unread;
            NotifyUnread();
        };
        _appWindows[app.Id] = win;
        TouchLru(app.Id);
        return win;
    }

    // ── Animación de apertura ──

    /// <summary>Ventana doble de esta ventana/monitor, creada una vez y reutilizada.</summary>
    private PanelRevealWindow? _proxy;

    /// <summary>Se incrementa en cada apertura: una animación vieja que "despierta" con un
    /// ticket distinto sabe que fue interrumpida y no toca nada.</summary>
    private int _revealTicket;

    private PanelRevealWindow Proxy
    {
        get
        {
            if (_proxy == null) { _proxy = new PanelRevealWindow(); _proxy.Prewarm(); }
            return _proxy;
        }
    }

    /// <summary>
    /// Anima la apertura en este orden, pensado para que no haya demora al hacer clic ni
    /// un instante "muerto" al final:
    /// 1. El doble (última captura de la app, o tarjeta lisa) aparece y empieza a crecer
    ///    desde el ícono EN EL PRIMER FRAME, antes de cualquier trabajo pesado.
    /// 2. Recién entonces se crea/muestra el panel real, encubierto, y empieza a cargar.
    /// 3. Antes del final, cuando el doble ya lo tapa casi entero, el panel real se
    ///    descubre, se enfoca y se repinta debajo: al llegar la animación ya es usable.
    /// 4. El doble se desvanece encima.
    /// </summary>
    private async Task RevealAsync(AppEntry app, AppHostWindow? existing, double originRelY,
                                   PanelAnimation anim, PanelGeometry.Rect? iconRectDip)
    {
        int ticket = _revealTicket;
        AppHostWindow? win = existing;
        var proxy = Proxy;
        try
        {
            // Destino calculado sin crear el panel (su creación es lo pesado).
            var target = existing?.TargetRect() ?? PanelGeometry.Compute(
                Anchor.BoundsDip, Placement, CurrentSideFor(app.Id),
                AppHostWindow.EffectiveWidthFor(app), AnchorRectFor(app.Id));

            PanelPreviewCache.TryGet(app.Id, out var preview);
            proxy.Prepare(app, preview, target, RevealOrigin(app.Id, iconRectDip),
                          fancy: anim == PanelAnimation.Fancy);
            var (firstFrame, uncover, done) = proxy.Play();

            await firstFrame;                       // el doble ya se ve y está animando
            if (ticket != _revealTicket) return;

            // Trabajo pesado del panel real, ahora que la animación ya arrancó.
            if (win != null) win.ShowCloaked();
            else { win = CreatePanel(app, originRelY, cloaked: true); win.Show(); }

            await uncover;
            if (ticket != _revealTicket) return;
            if (!win.Reveal()) { proxy.HideNow(); return; }   // se ocultó/cerró mientras tanto

            await done;
            if (ticket != _revealTicket) return;
            proxy.FadeOut();
        }
        catch
        {
            // Ante cualquier falla de la animación, el panel igual tiene que aparecer.
            if (ticket != _revealTicket) return;
            try { proxy.HideNow(); } catch { }
            if (win == null) { win = CreatePanel(app, originRelY, cloaked: false); win.Show(); }
            else if (!win.IsClosed) win.Reveal();
        }
    }

    /// <summary>
    /// Desde dónde crece el panel: el ícono que lo abrió en el dock; en Material, el botón
    /// flotante en su posición ACTUAL (se puede mover y cambia por ventana/monitor), así
    /// que se lee en cada apertura.
    /// </summary>
    private PanelGeometry.Rect RevealOrigin(string appId, PanelGeometry.Rect? iconRectDip)
    {
        if (!_dockMode)
        {
            const double fab = 56, halo = 4; // FAB de 56 dentro de su ventana de 64
            return new PanelGeometry.Rect(_button!.Left + halo, _button!.Top + halo, fab, fab);
        }
        if (iconRectDip is { } r) return r;

        // Atajo de teclado (sin clic): desde el centro de la barra / del ícono proyectado.
        var a = AnchorRectFor(appId);
        const double half = 22;
        return new PanelGeometry.Rect(a.CenterX - half, a.CenterY - half, half * 2, half * 2);
    }

    // ── Geometría de paneles según dock/botón ──
    // Se evalúan en cada llamada (no al crear el panel), así el panel se re-ancla bien
    // aunque el navegador, el botón o la barra se muevan.

    private PanelPlacement Placement => !_dockMode ? PanelPlacement.Beside : Anchor.DockEdge switch
    {
        DockEdge.Bottom => PanelPlacement.Above,
        DockEdge.Top    => PanelPlacement.Below,
        _               => PanelPlacement.Beside
    };

    private PanelGeometry.Rect AnchorRectFor(string appId)
    {
        if (!_dockMode)
        {
            const double winSize = 64; // ventana del botón (FAB 56 + halo)
            return new PanelGeometry.Rect(_button!.Left, _button!.Top, winSize, winSize);
        }

        var bar = _dock!.BarRect();
        if (!_dock.IsHorizontal) return bar;

        // Dock horizontal: el ícono de la app proyectado sobre la barra.
        double c = bar.Left + (_iconOffsets.TryGetValue(appId, out var off) ? off : bar.Width / 2);
        const double half = 22;
        return new PanelGeometry.Rect(c - half, bar.Top, half * 2, bar.Height);
    }

    private PanelSide CurrentSideFor(string appId)
    {
        if (!_dockMode) return ButtonSide;
        switch (Anchor.DockEdge)
        {
            case DockEdge.Left:  return PanelSide.Left;   // panel a la derecha de la barra
            case DockEdge.Right: return PanelSide.Right;  // panel a la izquierda de la barra
            default:
                // Horizontal: el borde fijo queda del lado del ícono más cercano al centro.
                var a = AnchorRectFor(appId);
                return a.CenterX >= Anchor.BoundsDip.CenterX ? PanelSide.Right : PanelSide.Left;
        }
    }

    // ── Modo Lite: límite de paneles vivos (LRU) ──

    private const int LiteLiveLimit = 3;
    private readonly List<string> _lru = new(); // más reciente al final

    private void TouchLru(string id)
    {
        _lru.Remove(id);
        _lru.Add(id);
    }

    private void EnforceLiveLimit()
    {
        if (!SettingsService.Current.LiteMode) return;

        // Los paneles fijados (KeepAlive) quedan SIEMPRE vivos: ni cuentan para el
        // cupo ni pueden ser cerrados. Solo los no-fijados compiten por el límite.
        int Closeable() => _appWindows.Values.Count(w => !w.KeepAlive);

        // Dejar lugar para el que está por abrirse: mantener (limit - 1) no-fijados.
        while (Closeable() >= LiteLiveLimit)
        {
            // El no-fijado menos usado recientemente (primero en el LRU que no esté fijado).
            var victim = _lru.FirstOrDefault(id =>
                _appWindows.TryGetValue(id, out var w) && !w.KeepAlive);

            if (victim == null) break; // no quedan no-fijados para cerrar

            _lru.Remove(victim);
            if (_appWindows.TryGetValue(victim, out var vw))
            {
                try { vw.ForceClose(); } catch { }
            }
        }
    }

    // ── Notificaciones (contador de no leídos) ──

    private readonly Dictionary<string, int> _unread = new();

    /// <summary>No leídos por appId (solo apps con panel abierto).</summary>
    public IReadOnlyDictionary<string, int> Unread => _unread;

    /// <summary>Total de no leídos de todas las apps abiertas.</summary>
    public int UnreadTotal => _unread.Values.Sum();

    /// <summary>Se dispara cuando cambia algún contador (lo escuchan botón y menú).</summary>
    public event Action? UnreadUpdated;

    /// <summary>True si hay algún panel de app visible (la barra dock se mantiene
    /// desplegada mientras esto sea true).</summary>
    public bool IsAnyPanelOpen => _appWindows.Values.Any(w => w.IsVisible);

    private void NotifyUnread()
    {
        bool show = SettingsService.Current.ShowBadges;
        _button?.SetBadge(show ? UnreadTotal : 0);
        _dock?.RebuildApps(); // refresca badges por app en la barra
        UnreadUpdated?.Invoke();
    }

    public void OpenAddAppDialog()
    {
        CloseMenu();

        var dlg = new AddAppDialog();
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            SettingsService.Current.Apps.Add(dlg.Result);
            SettingsService.Save();
            App.RefreshAppLists();
        }
    }

    public void RemoveApp(AppEntry app)
    {
        SettingsService.Current.Apps.RemoveAll(a => a.Id == app.Id);
        SettingsService.Save();
        CloseMenu();
        App.CloseAppPanels(app.Id);
        App.RefreshAppLists();
    }

    /// <summary>Editar nombre/URL de una app (desde el clic derecho del dock/menú).</summary>
    public void EditApp(AppEntry app)
    {
        CloseMenu();
        AppEditing.Edit(app, null);
    }

    /// <summary>Redibuja la lista de apps del dock o del menú abierto.</summary>
    public void RefreshApps()
    {
        _dock?.RebuildApps();
        if (_menuOpen) _menu?.Relayout();
    }

    /// <summary>Reaplica el layout del dock (tamaño) y re-ancla los paneles a la barra nueva.</summary>
    public void RefreshDockLayout()
    {
        _dock?.RefreshLayout();
        ReanchorOpenPanels();
    }

    /// <summary>Destruye el panel de una app en esta ventana (si existe).</summary>
    public void ClosePanel(string appId)
    {
        if (_appWindows.TryGetValue(appId, out var w))
        {
            try { w.ForceClose(); } catch { }
            _appWindows.Remove(appId);
        }
    }

    private SettingsWindow? _settingsWin;

    public void OpenSettings()
    {
        CloseMenu();

        // Si ya hay una ventana de Configuración abierta, traerla al frente
        // en vez de abrir otra.
        if (_settingsWin != null)
        {
            _settingsWin.Activate();
            return;
        }

        _settingsWin = new SettingsWindow(this);
        _settingsWin.Closed += (_, _) => _settingsWin = null;
        _settingsWin.Show();
    }

    /// <summary>Modo mover: arrastrar el botón flotante (Material) o la pestaña del dock
    /// a lo largo de su borde.</summary>
    public void EnterMoveMode()
    {
        if (_dockMode) _dock?.EnterMoveMode();
        else _button?.EnterMoveMode();
    }

    /// <summary>True si el cursor está sobre el área de referencia de este overlay (en
    /// escritorio con varios monitores decide a qué overlay van los atajos).</summary>
    public bool ContainsCursor()
    {
        if (!Win32.GetCursorPos(out var p)) return false;
        var b = Anchor.BoundsDip;
        double sc = Anchor is MonitorAnchor m ? m.Scale : Win32.DpiScaleOf(Anchor.OwnerHwnd);
        double x = p.X / sc, y = p.Y / sc;
        return x >= b.Left && x <= b.Right && y >= b.Top && y <= b.Bottom;
    }

    // ── Acciones para hotkeys ──

    /// <summary>Abre/enfoca una app por su Id (usado por hotkeys).</summary>
    public void OpenAppById(string id)
    {
        var app = SettingsService.Current.Apps.FirstOrDefault(a => a.Id == id);
        if (app != null) OpenApp(app, 0.5);
    }

    /// <summary>Oculta el panel de app actualmente activo/visible (usado por hotkey).</summary>
    public void HideActivePanel()
    {
        var w = _appWindows.Values.FirstOrDefault(x => x.IsActive)
             ?? _appWindows.Values.FirstOrDefault(x => x.IsVisible);
        w?.HideFromHotkey();
    }

    /// <summary>Activa/desactiva el auto-ocultar del botón flotante (usado por hotkey).</summary>
    public void ToggleAutoHide()
    {
        SettingsService.Current.AutoHide = !SettingsService.Current.AutoHide;
        SettingsService.Save();
    }

    /// <summary>Cicla a la app siguiente (+1) o anterior (-1) en la lista (usado por hotkey).</summary>
    public void CycleApp(int dir)
    {
        var apps = SettingsService.Current.Apps;
        if (apps.Count == 0) return;

        // Punto de partida: la app visible actual, o la primera.
        var currentId = _appWindows.Values.FirstOrDefault(x => x.IsVisible)?.AppId;
        int idx = currentId != null ? apps.FindIndex(a => a.Id == currentId) : -1;
        int next = ((idx + dir) % apps.Count + apps.Count) % apps.Count;
        OpenApp(apps[next], 0.5);
    }

    public void Dispose()
    {
        _disposed = true;
        CloseMenu();
        _revealTicket++;
        try { _proxy?.Close(); } catch { }
        _proxy = null;
        foreach (var w in _appWindows.Values)
        {
            try { w.ForceClose(); } catch { }
        }
        _appWindows.Clear();
        try { _button?.Close(); } catch { }
        try { _dock?.Close(); } catch { }
    }
}
