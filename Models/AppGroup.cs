namespace QuickPanel.Models;

/// <summary>Carpeta que agrupa apps en el menú (Complete).</summary>
public class AppGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Carpeta";

    /// <summary>Color de fondo de la carpeta (hex, ej "#3B82F6"). Vacío = color por defecto derivado del tema.</summary>
    public string Color { get; set; } = "";

    /// <summary>Orden manual de la carpeta para reordenarla junto a las apps. Menor = más arriba.</summary>
    public int Order { get; set; } = 0;
}
