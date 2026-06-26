using QuickPanel.Services;

namespace QuickPanel.Models;

public class QuickPanelSettings
{
    /// <summary>
    /// Identificador persistente y anónimo de esta instalación (GUID).
    /// Se envía al backend de licencias y se pasa como <c>quickpanel_customer_id</c>
    /// en el checkout de Paddle para vincular la compra. Se genera una sola vez
    /// en <see cref="QuickPanel.Services.SettingsService.Load"/>.
    /// </summary>
    public string CustomerId { get; set; } = "";

    /// <summary>Plan del usuario. Lo determina el backend de licencias (fuente de verdad).</summary>
    public LicenseTier Tier { get; set; } = LicenseTier.Free;

    /// <summary>Color semilla del esquema MD3 (hex).</summary>
    public string SeedColor { get; set; } = "#6366F1";

    /// <summary>Modo de tema: oscuro (default), claro o según el sistema.</summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;

    /// <summary>Tamaño de las ventanas de app (DIPs).</summary>
    public double PanelWidth { get; set; } = 440;
    public double PanelHeight { get; set; } = 760;

    /// <summary>Diámetro de los círculos del menú flotante en DIPs (Complete).</summary>
    public double MenuItemSize { get; set; } = 48;

    /// <summary>Posición del botón relativa al rect de la ventana de Edge (0..1).</summary>
    public double ButtonRelX { get; set; } = 0.97;
    public double ButtonRelY { get; set; } = 0.92;

    public bool RunAtStartup { get; set; } = true;

    /// <summary>Id de la app a abrir automáticamente al iniciar (Pro). Vacío = ninguna.</summary>
    public string StartAppId { get; set; } = "";

    /// <summary>Auto-ocultar el botón flotante cuando el mouse está lejos (Pro).</summary>
    public bool AutoHide { get; set; } = false;

    /// <summary>Mostrar contadores de no leídos (Pro). Desactivable.</summary>
    public bool ShowBadges { get; set; } = true;

    /// <summary>Modo Lite: optimiza RAM (suspende paneles ocultos, baja memoria,
    /// tope de paneles vivos, apaga autofill). Para equipos con poca memoria.</summary>
    public bool LiteMode { get; set; } = false;

    /// <summary>Atajos globales de acciones (Complete). Clave = nombre de HotkeyAction.</summary>
    public Dictionary<string, Hotkey> ActionHotkeys { get; set; } = new();

    public List<AppEntry> Apps { get; set; } = new();

    /// <summary>Carpetas para agrupar apps en el menú (Complete).</summary>
    public List<AppGroup> Groups { get; set; } = new();
}
