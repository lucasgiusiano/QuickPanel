using System.IO;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using DriveData = Google.Apis.Drive.v3.Data;

namespace QuickPanel.Services.CloudSync;

/// <summary>
/// Sincroniza el archivo de config contra la Application Data Folder de Google Drive
/// (espacio "appDataFolder": carpeta oculta, privada de la app, invisible al usuario).
///
/// Usa GoogleWebAuthorizationBroker, que levanta el LocalServerCodeReceiver (loopback
/// OAuth en http://localhost:PUERTO_LIBRE) y cachea el token en un FileDataStore local.
/// </summary>
public sealed class GoogleDriveSyncProvider : ICloudSyncProvider
{
    private const string AppName = "QuickPanel";
    private const string AppDataSpace = "appDataFolder";

    private UserCredential? _credential;

    public CloudProviderKind Kind => CloudProviderKind.GoogleDrive;
    public string DisplayName => "Google Drive";

    private static ClientSecrets Secrets => new()
    {
        ClientId = CloudSyncConstants.GoogleClientId,
        ClientSecret = CloudSyncConstants.GoogleClientSecret
    };

    private static string[] Scopes => new[] { CloudSyncConstants.GoogleAppDataScope };

    private static FileDataStore TokenStore => new(CloudSyncConstants.GoogleTokenStoreDir, fullPath: true);

    public async Task<bool> IsLinkedAsync(CancellationToken ct = default)
    {
        // Hay sesión si existe un token cacheado para "user" en el data store.
        try
        {
            var token = await TokenStore.GetAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>("user")
                .ConfigureAwait(false);
            return token != null && !string.IsNullOrEmpty(token.RefreshToken);
        }
        catch { return false; }
    }

    public async Task<string?> AuthenticateAsync(CancellationToken ct = default)
    {
        try
        {
            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                Secrets, Scopes, "user", ct, TokenStore).ConfigureAwait(false);

            if (_credential == null) return null;

            // Obtener el email de la cuenta para mostrarlo en la UI.
            using var svc = CreateService(_credential);
            var about = svc.About.Get();
            about.Fields = "user(emailAddress,displayName)";
            var info = await about.ExecuteAsync(ct).ConfigureAwait(false);
            return info.User?.EmailAddress ?? "Google Drive";
        }
        catch (OperationCanceledException) { return null; }
    }

    public async Task UnlinkAsync(CancellationToken ct = default)
    {
        try
        {
            if (_credential != null)
                await _credential.RevokeTokenAsync(ct).ConfigureAwait(false);
        }
        catch { /* si falla la revocación remota igual limpiamos local */ }

        _credential = null;
        try { await TokenStore.ClearAsync().ConfigureAwait(false); } catch { }
    }

    public async Task<CloudFileMetadata> GetMetadataAsync(CancellationToken ct = default)
    {
        var svc = await EnsureServiceAsync(ct).ConfigureAwait(false);
        var file = await FindConfigFileAsync(svc, ct).ConfigureAwait(false);
        return file == null ? CloudFileMetadata.NotFound : ToMetadata(file);
    }

    public async Task<string?> DownloadAsync(CancellationToken ct = default)
    {
        var svc = await EnsureServiceAsync(ct).ConfigureAwait(false);
        var file = await FindConfigFileAsync(svc, ct).ConfigureAwait(false);
        if (file == null) return null;

        using var ms = new MemoryStream();
        await svc.Files.Get(file.Id).DownloadAsync(ms, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public async Task<CloudFileMetadata> UploadAsync(string content, CancellationToken ct = default)
    {
        var svc = await EnsureServiceAsync(ct).ConfigureAwait(false);
        var existing = await FindConfigFileAsync(svc, ct).ConfigureAwait(false);

        var bytes = Encoding.UTF8.GetBytes(content);
        const string mime = "application/json";
        const string fields = "id,name,modifiedTime,size";

        DriveData.File result;
        using (var stream = new MemoryStream(bytes))
        {
            if (existing == null)
            {
                // Crear en el espacio appDataFolder.
                var meta = new DriveData.File
                {
                    Name = CloudSyncConstants.RemoteFileName,
                    Parents = new List<string> { AppDataSpace }
                };
                var req = svc.Files.Create(meta, stream, mime);
                req.Fields = fields;
                await req.UploadAsync(ct).ConfigureAwait(false);
                result = req.ResponseBody;
            }
            else
            {
                // Reemplazar contenido del archivo existente (sin tocar Parents).
                var meta = new DriveData.File { Name = CloudSyncConstants.RemoteFileName };
                var req = svc.Files.Update(meta, existing.Id, stream, mime);
                req.Fields = fields;
                await req.UploadAsync(ct).ConfigureAwait(false);
                result = req.ResponseBody;
            }
        }

        return ToMetadata(result);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private DriveService CreateService(UserCredential credential) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = AppName
    });

    private async Task<DriveService> EnsureServiceAsync(CancellationToken ct)
    {
        _credential ??= await GoogleWebAuthorizationBroker.AuthorizeAsync(
            Secrets, Scopes, "user", ct, TokenStore).ConfigureAwait(false);
        return CreateService(_credential);
    }

    /// <summary>Busca el archivo de config en el espacio appData. null si no existe.</summary>
    private static async Task<DriveData.File?> FindConfigFileAsync(DriveService svc, CancellationToken ct)
    {
        var list = svc.Files.List();
        list.Spaces = "appDataFolder";
        list.Q = $"name = '{CloudSyncConstants.RemoteFileName}'";
        list.Fields = "files(id,name,modifiedTime,size)";
        list.PageSize = 10;

        var res = await list.ExecuteAsync(ct).ConfigureAwait(false);
        return res.Files is { Count: > 0 } ? res.Files[0] : null;
    }

    private static CloudFileMetadata ToMetadata(DriveData.File file) => new()
    {
        Exists = true,
        FileId = file.Id,
        // Drive no expone un ETag HTTP estable acá; para la detección de conflictos de
        // Fase 2 se usará el ModifiedUtc. Guardamos el timestamp ISO como "ETag" lógico.
        ETag = file.ModifiedTimeRaw ?? "",
        ModifiedUtc = file.ModifiedTimeDateTimeOffset,
        Size = file.Size ?? 0
    };
}
