using QuickPanel.Services;

namespace QuickPanel.Models;

public class QuickPanelSettings
{
    /// <summary>Plan del usuario. Se actualiza desde la Store al verificar IAP.</summary>
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

    public List<AppEntry> Apps { get; set; } = new();
}
