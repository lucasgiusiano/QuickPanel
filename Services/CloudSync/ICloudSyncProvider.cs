namespace QuickPanel.Services.CloudSync;

/// <summary>Identifica cuál proveedor concreto está en uso.</summary>
public enum CloudProviderKind
{
    None,
    GoogleDrive,
    OneDrive
}

/// <summary>
/// Contrato común para sincronizar el archivo de configuración contra el
/// almacenamiento privado del usuario (Google Drive appDataFolder / OneDrive approot).
///
/// Fase 1: se usan Authenticate / GetMetadata / Download / Upload / Unlink para un
/// flujo manual subir-bajar sin lógica de conflicto. La metadata (ETag/timestamp) ya
/// se expone acá para no cambiar la firma en la Fase 2 (merge por campo + tombstones).
/// </summary>
public interface ICloudSyncProvider
{
    /// <summary>Qué proveedor concreto es esta instancia.</summary>
    CloudProviderKind Kind { get; }

    /// <summary>Nombre legible del proveedor (para la UI).</summary>
    string DisplayName { get; }

    /// <summary>
    /// True si ya hay una sesión válida cacheada (token en caché cifrada), sin
    /// abrir el navegador. No garantiza conectividad, solo credenciales presentes.
    /// </summary>
    Task<bool> IsLinkedAsync(CancellationToken ct = default);

    /// <summary>
    /// Autentica al usuario. Si hay token en caché lo reutiliza en silencio; si no,
    /// abre el navegador del sistema (loopback OAuth). Devuelve el email/UPN de la
    /// cuenta vinculada, o null si el usuario canceló.
    /// </summary>
    Task<string?> AuthenticateAsync(CancellationToken ct = default);

    /// <summary>Borra la sesión local (token en caché). No revoca del lado del proveedor.</summary>
    Task UnlinkAsync(CancellationToken ct = default);

    /// <summary>Consulta la metadata del archivo de config en la nube. Requiere sesión válida.</summary>
    Task<CloudFileMetadata> GetMetadataAsync(CancellationToken ct = default);

    /// <summary>
    /// Descarga el contenido del archivo de config desde la nube.
    /// Devuelve null si el archivo no existe en la nube.
    /// </summary>
    Task<string?> DownloadAsync(CancellationToken ct = default);

    /// <summary>
    /// Sube (crea o reemplaza) el archivo de config con el contenido dado.
    /// Devuelve la metadata resultante (con el ETag nuevo).
    /// </summary>
    Task<CloudFileMetadata> UploadAsync(string content, CancellationToken ct = default);
}
