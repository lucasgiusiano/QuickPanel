using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;

namespace QuickPanel.Services;

/// <summary>
/// Pide una reseña una sola vez por versión nueva instalada, nunca en la primera instalación
/// y nunca más una vez que el usuario hizo clic en "Reseñar" (no hay forma de confirmar que la
/// reseña se envió de verdad — ni la API nativa ni el deep link a la Store devuelven ese dato —
/// así que se asume que reseñó apenas abre el flujo).
///
/// Estado local per-device (no en QuickPanelSettings / Cloud Sync): igual que
/// <see cref="SettingsService.LastSyncSuccessUtc"/>, "qué versión ya vio y si ya reseñó ESTA PC"
/// es un dato propio de cada equipo. Sincronizarlo generaría falsos negativos/positivos cruzando
/// dispositivos que se actualizan en momentos distintos.
/// </summary>
public static class ReviewPromptService
{
    private const string ProductId = "9N3Z0WKL8KPN"; // SidePanel for Browsers

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickPanel");
    private static readonly string FilePath = Path.Combine(Dir, "review.json");

    private sealed class State
    {
        public string LastSeenVersion { get; set; } = "";
        public bool Reviewed { get; set; }
    }

    private static State _state = new();

    /// <summary>
    /// Llamar una vez al arrancar. Decide en el momento (para no re-preguntar si el usuario
    /// reinicia varias veces en la misma versión) y, si corresponde, programa el prompt con
    /// un delay para no interrumpir el arranque.
    /// </summary>
    public static void CheckOnStartup()
    {
        Load();

        string current = StartupService.AppVersionString;
        bool firstRun = string.IsNullOrEmpty(_state.LastSeenVersion);
        bool isUpdate = !firstRun && _state.LastSeenVersion != current;
        bool shouldAsk = isUpdate && !_state.Reviewed;

        _state.LastSeenVersion = current;
        Save();

        if (!shouldAsk) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            new Settings.ReviewPromptWindow().Show();
        };
        timer.Start();
    }

    /// <summary>
    /// El usuario hizo clic en "Reseñar": se asume reseñado y no se vuelve a preguntar.
    /// Con Store instalada desde la Store, abre el diálogo nativo de calificación (no sale de
    /// la app); si no, o si la API falla, cae al deep link de la ficha en la Store.
    /// </summary>
    public static async void OpenReview(IntPtr ownerHwnd)
    {
        _state.Reviewed = true;
        Save();

        if (StartupService.IsPackaged)
        {
            try
            {
                var ctx = Windows.Services.Store.StoreContext.GetDefault();
                // Requerido en apps de escritorio (Win32) con más de una ventana: sin esto,
                // StoreContext no sabe sobre qué ventana anclar su propio diálogo modal.
                ((IInitializeWithWindow)(object)ctx).Initialize(ownerHwnd);
                await ctx.RequestRateAndReviewAppAsync();
                return;
            }
            catch { /* API no disponible en este contexto: caer al deep link */ }
        }

        try
        {
            Process.Start(new ProcessStartInfo($"ms-windows-store://review/?ProductId={ProductId}")
            {
                UseShellExecute = true
            });
        }
        catch { /* sin Store instalada: no hay nada más que ofrecer */ }
    }

    private static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                _state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { _state = new(); }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_state));
        }
        catch { /* no romper la app por IO */ }
    }

    [ComImport, Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithWindow
    {
        void Initialize(IntPtr hwnd);
    }
}
