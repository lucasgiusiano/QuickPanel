namespace QuickPanel.Services.CloudSync;

/// <summary>
/// Metadatos del archivo de configuración tal como existe en la nube.
/// Se usa en Fase 1 solo para informar al usuario (fecha, existe/no existe);
/// el <see cref="ETag"/> queda expuesto para la resolución de conflictos de Fase 2.
/// </summary>
public sealed record CloudFileMetadata
{
    /// <summary>True si el archivo existe en la nube.</summary>
    public bool Exists { get; init; }

    /// <summary>Id del archivo en el proveedor (Drive fileId / Graph itemId). Vacío si no existe.</summary>
    public string FileId { get; init; } = "";

    /// <summary>
    /// ETag/versión que devuelve el proveedor. Cambia en cada modificación.
    /// Base de la detección de colisiones (Fase 2). Vacío si no existe.
    /// </summary>
    public string ETag { get; init; } = "";

    /// <summary>Última modificación reportada por la nube (UTC). null si no existe.</summary>
    public DateTimeOffset? ModifiedUtc { get; init; }

    /// <summary>Tamaño en bytes reportado por la nube. 0 si no existe.</summary>
    public long Size { get; init; }

    public static CloudFileMetadata NotFound => new() { Exists = false };
}
