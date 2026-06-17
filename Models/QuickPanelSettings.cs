using QuickPanel.Services;

namespace QuickPanel.Models;

public class QuickPanelSettings
{
    /// <summary>Plan del usuario. Se actualiza desde la Store al verificar IAP.</summary>
    public LicenseTier Tier { get; set; } = LicenseTier.Free;

    /// <summary>Color semilla del esquema MD3 (hex).</summary>
    public string SeedColor { get; set; } = "#6366F1";

    /// <summary>Tamaño de las ventanas de app (DIPs).</summary>
    public double PanelWidth { get; set; } = 440;
    public double PanelHeight { get; set; } = 760;

    /// <summary>Posición del botón relativa al rect de la ventana de Edge (0..1).</summary>
    public double ButtonRelX { get; set; } = 0.97;
    public double ButtonRelY { get; set; } = 0.92;

    public bool RunAtStartup { get; set; } = true;

    public List<AppEntry> Apps { get; set; } = new();
}
