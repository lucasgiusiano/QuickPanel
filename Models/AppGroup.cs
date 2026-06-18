namespace QuickPanel.Models;

/// <summary>Carpeta que agrupa apps en el menú (Complete).</summary>
public class AppGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Carpeta";
}
