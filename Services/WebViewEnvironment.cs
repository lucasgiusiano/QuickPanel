using Microsoft.Web.WebView2.Core;
using System.IO;

namespace QuickPanel.Services;

/// <summary>
/// Provee un único CoreWebView2Environment compartido por toda la app.
///
/// Antes cada panel creaba su propio environment con un UserDataFolder distinto,
/// lo que multiplicaba los procesos base de Chromium (GPU, red, storage, audio…)
/// por cada app abierta. Compartiendo un solo environment + UserDataFolder, esos
/// procesos base se comparten entre todos los paneles; el aislamiento de sesión
/// (cookies, login) se mantiene usando un CoreWebView2Profile nombrado por app.
/// </summary>
public static class WebViewEnvironment
{
    private static CoreWebView2Environment? _env;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Carpeta de datos compartida para todos los perfiles.</summary>
    public static string SharedUserDataFolder =>
        Path.Combine(SettingsService.ProfilesDir, "Shared");

    /// <summary>Obtiene (o crea una sola vez) el environment compartido.</summary>
    public static async Task<CoreWebView2Environment> GetAsync()
    {
        if (_env != null) return _env;
        await _gate.WaitAsync();
        try
        {
            if (_env == null)
            {
                Directory.CreateDirectory(SharedUserDataFolder);
                _env = await CoreWebView2Environment.CreateAsync(null, SharedUserDataFolder);
            }
            return _env;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Nombre de perfil válido para una app. Los perfiles permiten letras,
    /// dígitos, guión bajo, guión y punto; máx. 64 chars.</summary>
    public static string ProfileNameFor(string appId)
    {
        var clean = new string(appId.Where(c =>
            char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.').ToArray());
        if (clean.Length == 0) clean = "app";
        return clean.Length > 64 ? clean[..64] : clean;
    }
}
