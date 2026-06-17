using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using QuickPanel.Core;
using QuickPanel.Models;
using QuickPanel.Services;

namespace QuickPanel.AppWindow;

public partial class AppHostWindow : Window
{
    private readonly AppEntry _app;
    private readonly IntPtr   _edgeHwnd;
    private readonly PanelSide _side;
    private readonly double   _originRelY;
    private readonly Func<double, PanelGeometry.Rect> _computeBounds; // width -> geometría
    private readonly Func<double> _maxWidth;
    private bool _forceClose;

    /// <summary>Id de la app que hostea esta ventana.</summary>
    public string AppId => _app.Id;

    /// <summary>Cantidad de no leídos detectada en el título (0 si no hay).</summary>
    public int Unread { get; private set; }

    /// <summary>Se dispara cuando cambia el conteo de no leídos.</summary>
    public event Action<AppHostWindow>? UnreadChanged;

    /// <summary>Oculta el panel (invocado por hotkey).</summary>
    public void HideFromHotkey() => HidePanel();

    public AppHostWindow(
        AppEntry app, IntPtr edgeHwnd, PanelSide side, double originRelY,
        Func<double, PanelGeometry.Rect> computeBounds, Func<double> maxWidth)
    {
        _app           = app;
        _edgeHwnd      = edgeHwnd;
        _side          = side;
        _originRelY    = Math.Clamp(originRelY, 0.05, 0.95);
        _computeBounds = computeBounds;
        _maxWidth      = maxWidth;
        InitializeComponent();

        TitleText.Text = app.Name;
        LoadIcon();
        ConfigureGripSide();

        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) =>
        {
            AnchorToEdge();
            await InitWebViewAsync();
            ForceWebViewRepaint();
        };
    }

    private void ConfigureGripSide()
    {
        if (_side == PanelSide.Right)
        {
            GripLeftCol.Width = new GridLength(6);
            GripLeft.Opacity  = 1;
            RootHost.RenderTransformOrigin = new Point(1, _originRelY);
        }
        else
        {
            GripRightCol.Width = new GridLength(6);
            GripRight.Opacity  = 1;
            RootHost.RenderTransformOrigin = new Point(0, _originRelY);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_edgeHwnd != IntPtr.Zero)
            Win32.SetWindowLongPtr(hwnd, Win32.GWLP_HWNDPARENT, _edgeHwnd);

        // TOOLWINDOW evita que la ventana minimizada aparezca como recuadro
        // en la esquina inferior izquierda (estilo Windows clásico).
        var ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE,
            new IntPtr(ex | Win32.WS_EX_TOOLWINDOW));
    }

    public void AnchorToEdge()
    {
        if (!Win32.IsWindow(_edgeHwnd)) return;
        double w = Math.Max(PanelGeometry.MinPanel, SettingsService.Current.PanelWidth);
        var g = _computeBounds(w);
        Width  = g.Width;
        Height = g.Height;
        Left   = g.Left;
        Top    = g.Top;
    }

    public void Reanchor()
    {
        if (_hidden) return;
        AnchorToEdge();
    }

    // ── Resize de un solo lado (borde fijo = lado del menú) ──

    private void GripLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newW = Width - e.HorizontalChange;
        Apply(newW, anchorRight: Left + Width);
    }

    private void GripRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newW = Width + e.HorizontalChange;
        Apply(newW, anchorLeft: Left);
    }

    private void Apply(double newW, double? anchorRight = null, double? anchorLeft = null)
    {
        double maxW = _maxWidth();
        newW = Math.Clamp(newW, PanelGeometry.MinPanel, maxW);

        if (anchorRight is { } ar) Left = ar - newW;
        else if (anchorLeft is { } al) Left = al;

        Width = newW;
        SettingsService.Current.PanelWidth = newW;
        SettingsService.Save();
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            var profileDir = Path.Combine(SettingsService.ProfilesDir, Sanitize(_app.Id));
            Directory.CreateDirectory(profileDir);

            var env = await CoreWebView2Environment.CreateAsync(null, profileDir);
            await Web.EnsureCoreWebView2Async(env);

            var s = Web.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled    = true;
            s.IsStatusBarEnabled               = false;
            s.AreBrowserAcceleratorKeysEnabled = true;
            s.IsGeneralAutofillEnabled         = true;
            s.IsPasswordAutosaveEnabled        = true;

            Web.CoreWebView2.NewWindowRequested += (_, ev) =>
            {
                ev.Handled = true;
                Web.CoreWebView2.Navigate(ev.Uri);
            };

            Web.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                var t = Web.CoreWebView2.DocumentTitle;
                if (!string.IsNullOrWhiteSpace(t)) TitleText.Text = t;
                UpdateUnread(t);
            };

            // Historial (Pro): registrar cada navegación de nivel superior.
            Web.CoreWebView2.SourceChanged += (_, _) =>
            {
                if (!LicenseService.HasFeature(Feature.History)) return;
                RecordHistory(Web.CoreWebView2.Source);
            };

            // WhatsApp Web invalida la sesión si el almacenamiento no es "persistente".
            // Pedimos persistencia explícita en cada documento que carga.
            await Web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "if (navigator.storage && navigator.storage.persist) { navigator.storage.persist(); }");

            Web.CoreWebView2.Navigate(_app.Url);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo iniciar WebView2:\n{ex.Message}",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Error);
            ForceClose();
        }
    }

    private void LoadIcon()
    {
        try
        {
            var url = string.IsNullOrEmpty(_app.Favicon) ? AppEntry.FaviconFor(_app.Url) : _app.Favicon;
            if (string.IsNullOrEmpty(url)) return;
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource   = new Uri(url);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            TitleIcon.Source = img;
        }
        catch { }
    }

    private static string Sanitize(string s) =>
        string.Concat(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));

    private bool _hidden;

    public void ShowAndFocus()
    {
        _hidden = false;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Opacity = 1;

        // ORDEN CLAVE: mostrar primero, luego posicionar, luego forzar repintado.
        // Redimensionar la ventana mientras está oculta deja el WebView2 en blanco
        // (optimización interna que no re-renderiza hasta recibir foco/resize).
        Show();
        AnchorToEdge();
        Activate();
        Topmost = true; Topmost = false;
        ForceWebViewRepaint();
    }

    /// <summary>
    /// Oculta el panel con Hide() sin tocar tamaño/posición (reposicionar oculto
    /// es lo que deja el WebView en blanco). No destruye el WebView: conserva
    /// sesión y página en memoria.
    /// </summary>
    private void HidePanel()
    {
        _hidden = true;
        Hide();
    }

    /// <summary>
    /// Fuerza a WebView2 a re-renderizar tras mostrar/posicionar la ventana.
    /// El control WPF no expone UpdateWindowPos, así que provocamos el repintado
    /// con un micro-cambio de layout que el motor sí detecta.
    /// </summary>
    private void ForceWebViewRepaint()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            try
            {
                // Nudge de 1px: cambia el tamaño y lo restaura en el siguiente frame,
                // lo que dispara el re-render interno del WebView.
                var w = Width;
                Width = w - 1;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
                {
                    Width = w;
                });

                Web.InvalidateVisual();
            }
            catch { }
        });
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Reload();
    private void BtnClose_Click(object sender, RoutedEventArgs e)  => HidePanel();

    /// <summary>
    /// Extrae el conteo de no leídos del título de la web app. La mayoría usa
    /// formatos como "(3) WhatsApp", "WhatsApp (3)" o "• Inbox (12)".
    /// </summary>
    private void UpdateUnread(string? title)
    {
        int count = 0;
        if (!string.IsNullOrEmpty(title))
        {
            var m = System.Text.RegularExpressions.Regex.Match(title, @"\((\d+)\)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) count = n;
        }
        if (count == Unread) return;
        Unread = count;
        UnreadChanged?.Invoke(this);
    }

    private void RecordHistory(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var h = _app.History;
        h.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase));
        h.Insert(0, url);
        if (h.Count > 20) h.RemoveRange(20, h.Count - 20);
        SettingsService.Save();
    }

    private void BtnHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!LicenseService.HasFeature(Feature.History))
        {
            new Settings.UpgradeWindow("El historial de navegación es parte del plan Pro.").ShowDialog();
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu();
        if (_app.History.Count == 0)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem
            { Header = "Sin historial todavía", IsEnabled = false });
        }
        else
        {
            foreach (var url in _app.History)
            {
                var item = new System.Windows.Controls.MenuItem { Header = Shorten(url) };
                var target = url;
                item.Click += (_, _) => Web.CoreWebView2?.Navigate(target);
                menu.Items.Add(item);
            }
        }
        menu.PlacementTarget = BtnHistory;
        menu.IsOpen = true;
    }

    private static string Shorten(string url)
    {
        try
        {
            var u = new Uri(url);
            var s = u.Host + u.PathAndQuery;
            return s.Length > 60 ? s[..57] + "…" : s;
        }
        catch { return url.Length > 60 ? url[..57] + "…" : url; }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Esperamos un frame: si el foco fue a otra ventana de QuickPanel
        // (el botón flotante, el menú, otro panel), no ocultamos.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (_forceClose || _hidden) return;
            var active = System.Windows.Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive);

            bool focusOnOwnWindow = active != null && active != this;
            if (!focusOnOwnWindow) HidePanel();
        });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            HidePanel();
            return;
        }
        try { Web.Dispose(); } catch { }
        base.OnClosing(e);
    }
}
