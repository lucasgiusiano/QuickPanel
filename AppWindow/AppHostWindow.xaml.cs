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
            PlayOpenAnimation();
            await InitWebViewAsync();
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
        if (!IsVisible) return;
        AnchorToEdge();
    }

    private void PlayOpenAnimation()
    {
        var dur  = TimeSpan.FromMilliseconds(280);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        HostScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.0, 1.0, dur) { EasingFunction = ease });
        HostScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.35, 1.0, dur) { EasingFunction = ease });
        RootHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180)));
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

            Web.CoreWebView2.NewWindowRequested += (_, ev) =>
            {
                ev.Handled = true;
                Web.CoreWebView2.Navigate(ev.Uri);
            };

            Web.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                var t = Web.CoreWebView2.DocumentTitle;
                if (!string.IsNullOrWhiteSpace(t)) TitleText.Text = t;
            };

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

    public void ShowAndFocus()
    {
        AnchorToEdge();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        PlayOpenAnimation();
        Activate();
        Topmost = true; Topmost = false;
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Reload();
    private void BtnClose_Click(object sender, RoutedEventArgs e)  => Hide();

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Esperamos un frame: si el foco fue a otra ventana de QuickPanel
        // (el botón flotante, el menú, otro panel), no ocultamos.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (_forceClose) return;
            var active = System.Windows.Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive);

            bool focusOnOwnWindow = active != null && active != this;
            if (!focusOnOwnWindow) Hide();
        });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        try { Web.Dispose(); } catch { }
        base.OnClosing(e);
    }
}
