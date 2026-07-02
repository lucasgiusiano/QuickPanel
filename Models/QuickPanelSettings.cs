using QuickPanel.Services;

namespace QuickPanel.Models;

/// <summary>Estilo del menú de apps.</summary>
public enum MenuMode
{
    /// <summary>Botón flotante redondo + menú radial (Material Design). Default.</summary>
    Material,
    /// <summary>Barra lateral auto-ocultable anclada al borde derecho del navegador.</summary>
    Dock
}

public class QuickPanelSettings
{
    /// <summary>Color semilla del esquema MD3 (hex).</summary>
    public string SeedColor { get; set; } = "#6366F1";

    /// <summary>Modo de tema: oscuro (default), claro o según el sistema.</summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;

    /// <summary>Tamaño de las ventanas de app (DIPs).</summary>
    public double PanelWidth { get; set; } = 440;
    public double PanelHeight { get; set; } = 760;

    /// <summary>Diámetro de los círculos del menú flotante en DIPs.</summary>
    public double MenuItemSize { get; set; } = 48;

    /// <summary>Estilo del menú: botón flotante Material (default) o barra Dock lateral.</summary>
    public MenuMode MenuMode { get; set; } = MenuMode.Material;

    /// <summary>Idioma de la interfaz. null = automático (seguir el idioma de Windows).</summary>
    public Lang? Language { get; set; } = null;

    /// <summary>Posición del botón relativa al rect de la ventana de Edge (0..1).</summary>
    public double ButtonRelX { get; set; } = 0.97;
    public double ButtonRelY { get; set; } = 0.92;

    public bool RunAtStartup { get; set; } = true;

    /// <summary>Id de la app a abrir automáticamente al iniciar. Vacío = ninguna.</summary>
    public string StartAppId { get; set; } = "";

    /// <summary>Auto-ocultar el botón flotante cuando el mouse está lejos.</summary>
    public bool AutoHide { get; set; } = false;

    /// <summary>Mostrar contadores de no leídos. Desactivable.</summary>
    public bool ShowBadges { get; set; } = true;

    /// <summary>Modo Lite: optimiza RAM (suspende paneles ocultos, baja memoria,
    /// tope de paneles vivos, apaga autofill). Para equipos con poca memoria.</summary>
    public bool LiteMode { get; set; } = false;

    /// <summary>Atajos globales de acciones. Clave = nombre de HotkeyAction.</summary>
    public Dictionary<string, Hotkey> ActionHotkeys { get; set; } = new();

    public List<AppEntry> Apps { get; set; } = new();

    /// <summary>Carpetas para agrupar apps en el menú.</summary>
    public List<AppGroup> Groups { get; set; } = new();

    // ── Cloud Sync (Fase 1) ────────────────────────────────────────

    /// <summary>Proveedor de sync vinculado: "None" (default), "GoogleDrive" u "OneDrive".</summary>
    public string CloudProvider { get; set; } = "None";

    /// <summary>Email/UPN de la cuenta vinculada, para mostrarlo en la UI. Vacío = sin vincular.</summary>
    public string CloudAccount { get; set; } = "";

    // ── Cloud Sync (Fase 2) ────────────────────────────────────────

    /// <summary>Cada cuándo sincronizar automáticamente. Default: al cerrar la app.</summary>
    public Services.CloudSync.SyncInterval SyncInterval { get; set; }
        = Services.CloudSync.SyncInterval.OnAppClose;

    /// <summary>
    /// Journal de metadata de sync (timestamps por Id, tombstones, timestamp global).
    /// Separado de los modelos de dominio.
    /// </summary>
    public Services.CloudSync.SyncJournal SyncJournal { get; set; } = new();

    /// <summary>
    /// Parsea <see cref="CloudProvider"/> al enum de sync sin acoplar el modelo a
    /// la capa de servicios. Valores desconocidos = None.
    /// </summary>
    public static Services.CloudSync.CloudProviderKind ParseProvider(string? value) => value switch
    {
        "GoogleDrive" => Services.CloudSync.CloudProviderKind.GoogleDrive,
        "OneDrive"    => Services.CloudSync.CloudProviderKind.OneDrive,
        _             => Services.CloudSync.CloudProviderKind.None
    };
}
