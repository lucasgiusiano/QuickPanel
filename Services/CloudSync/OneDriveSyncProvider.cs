using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace QuickPanel.Services.CloudSync;

/// <summary>
/// Sincroniza el archivo de config contra la App Folder de OneDrive
/// (/me/drive/special/approot: carpeta exclusiva de la app dentro del OneDrive del usuario).
///
/// Autentica con MSAL.NET (PublicClientApplication) usando el navegador del sistema
/// (redirect loopback http://localhost). Los tokens se cachean cifrados en disco vía
/// MsalCacheHelper (DPAPI en Windows). Las llamadas a Graph se hacen por REST con HttpClient
/// para no arrastrar todo el SDK de Graph.
/// </summary>
public sealed class OneDriveSyncProvider : ICloudSyncProvider
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private static string ItemPath => $"/me/drive/special/approot:/{CloudSyncConstants.RemoteFileName}";

    private static readonly HttpClient Http = new();

    private IPublicClientApplication? _app;

    public CloudProviderKind Kind => CloudProviderKind.OneDrive;
    public string DisplayName => "OneDrive";

    // ── MSAL setup ─────────────────────────────────────────────────

    private async Task<IPublicClientApplication> GetAppAsync()
    {
        if (_app != null) return _app;

        var app = PublicClientApplicationBuilder
            .Create(CloudSyncConstants.MicrosoftClientId)
            .WithAuthority(CloudSyncConstants.MicrosoftAuthority)
            .WithRedirectUri("http://localhost")
            .Build();

        // Caché de tokens persistente y cifrada (DPAPI en Windows).
        Directory.CreateDirectory(CloudSyncConstants.MsalCacheDir);
        var storage = new StorageCreationPropertiesBuilder(
            CloudSyncConstants.MsalCacheFileName,
            CloudSyncConstants.MsalCacheDir).Build();
        var cacheHelper = await MsalCacheHelper.CreateAsync(storage).ConfigureAwait(false);
        cacheHelper.RegisterCache(app.UserTokenCache);

        _app = app;
        return app;
    }

    private async Task<AuthenticationResult?> AcquireAsync(bool allowInteractive, CancellationToken ct)
    {
        var app = await GetAppAsync().ConfigureAwait(false);
        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        var account = accounts.FirstOrDefault();

        try
        {
            if (account != null)
                return await app.AcquireTokenSilent(CloudSyncConstants.MicrosoftScopes, account)
                    .ExecuteAsync(ct).ConfigureAwait(false);
        }
        catch (MsalUiRequiredException) { /* cae a interactivo */ }

        if (!allowInteractive) return null;

        try
        {
            return await app.AcquireTokenInteractive(CloudSyncConstants.MicrosoftScopes)
                .WithUseEmbeddedWebView(false) // navegador del sistema (loopback)
                .ExecuteAsync(ct).ConfigureAwait(false);
        }
        catch (MsalClientException) { return null; } // usuario canceló
        catch (OperationCanceledException) { return null; }
    }

    // ── ICloudSyncProvider ─────────────────────────────────────────

    public async Task<bool> IsLinkedAsync(CancellationToken ct = default)
    {
        var res = await AcquireAsync(allowInteractive: false, ct).ConfigureAwait(false);
        return res != null;
    }

    public async Task<string?> AuthenticateAsync(CancellationToken ct = default)
    {
        var res = await AcquireAsync(allowInteractive: true, ct).ConfigureAwait(false);
        return res?.Account?.Username; // email/UPN de la cuenta
    }

    public async Task UnlinkAsync(CancellationToken ct = default)
    {
        var app = await GetAppAsync().ConfigureAwait(false);
        foreach (var acc in await app.GetAccountsAsync().ConfigureAwait(false))
        {
            try { await app.RemoveAsync(acc).ConfigureAwait(false); } catch { }
        }
    }

    public async Task<CloudFileMetadata> GetMetadataAsync(CancellationToken ct = default)
    {
        using var req = await BuildRequestAsync(HttpMethod.Get, $"{GraphBase}{ItemPath}", ct)
            .ConfigureAwait(false);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotFound) return CloudFileMetadata.NotFound;
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseMetadata(json);
    }

    public async Task<string?> DownloadAsync(CancellationToken ct = default)
    {
        using var req = await BuildRequestAsync(HttpMethod.Get, $"{GraphBase}{ItemPath}:/content", ct)
            .ConfigureAwait(false);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();

        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public async Task<CloudFileMetadata> UploadAsync(string content, CancellationToken ct = default)
    {
        // Archivos chicos (< 4 MB): PUT simple al endpoint :/content.
        using var req = await BuildRequestAsync(HttpMethod.Put, $"{GraphBase}{ItemPath}:/content", ct)
            .ConfigureAwait(false);
        req.Content = new StringContent(content, Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseMetadata(json);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string url, CancellationToken ct)
    {
        var res = await AcquireAsync(allowInteractive: false, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("OneDrive no está vinculado.");
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", res.AccessToken);
        return req;
    }

    private static CloudFileMetadata ParseMetadata(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        string etag = root.TryGetProperty("eTag", out var etagEl) ? etagEl.GetString() ?? "" : "";
        long size = root.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
        DateTimeOffset? modified = null;
        if (root.TryGetProperty("lastModifiedDateTime", out var modEl) &&
            modEl.TryGetDateTimeOffset(out var m))
            modified = m;

        return new CloudFileMetadata
        {
            Exists = true,
            FileId = id,
            ETag = etag,
            ModifiedUtc = modified,
            Size = size
        };
    }
}
