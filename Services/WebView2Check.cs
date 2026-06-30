using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace QuickPanel.Services;

/// <summary>Verifica que el runtime de WebView2 esté disponible antes de arrancar.</summary>
public static class WebView2Check
{
    private const string DownloadUrl =
        "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>
    /// Devuelve true si WebView2 está disponible. Si no, ofrece abrir la descarga
    /// y devuelve false (el caller debe abortar el arranque).
    /// </summary>
    public static bool EnsureAvailable()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch
        {
            var r = MessageBox.Show(
                Loc.T("WebView2_Missing"),
                "QuickPanel",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (r == MessageBoxResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true }); }
                catch { /* ignore */ }
            }
            return false;
        }
    }
}
