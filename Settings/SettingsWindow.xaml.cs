using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using QuickPanel.Core;
using QuickPanel.Services;

namespace QuickPanel.Settings;

public partial class SettingsWindow : Window
{
    private readonly OverlayManager? _manager;

    private static readonly string[] SeedPalette =
    {
        "#6366F1", "#8B5CF6", "#EC4899", "#EF4444", "#F59E0B",
        "#10B981", "#06B6D4", "#3B82F6", "#64748B", "#14B8A6"
    };

    public SettingsWindow(OverlayManager? manager)
    {
        _manager = manager;
        InitializeComponent();
        BuildSwatches();
        LoadState();

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"QuickPanel v{v?.Major}.{v?.Minor}.{v?.Build}";
    }

    private void BuildSwatches()
    {
        foreach (var hex in SeedPalette)
        {
            var c   = (Color)ColorConverter.ConvertFromString(hex);
            var dot = new Ellipse
            {
                Width           = 34,
                Height          = 34,
                Margin          = new Thickness(4),
                Fill            = new SolidColorBrush(c),
                Cursor          = Cursors.Hand,
                Stroke          = Brushes.Transparent,
                StrokeThickness = 3
            };

            if (string.Equals(hex, SettingsService.Current.SeedColor, StringComparison.OrdinalIgnoreCase))
                dot.Stroke = (Brush)FindResource("Md3OnSurface");

            dot.MouseLeftButtonUp += (_, _) =>
            {
                SettingsService.Current.SeedColor = hex;
                SettingsService.Save();
                ThemeService.Apply(hex);
                foreach (var child in Swatches.Children.OfType<Ellipse>())
                    child.Stroke = Brushes.Transparent;
                dot.Stroke = (Brush)FindResource("Md3OnSurface");
            };

            Swatches.Children.Add(dot);
        }
    }

    private void LoadState()
    {
        var s = SettingsService.Current;
        (s.PanelWidth switch
        {
            <= 360 => SizeS,
            >= 520 => SizeL,
            _      => SizeM
        }).IsChecked = true;

        ChkStartup.IsChecked = s.RunAtStartup;
    }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null)
        {
            MessageBox.Show("Abrí la configuración desde el botón flotante para mover su posición.",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _manager.EnterMoveMode();
        Close();
    }

    private void Size_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            var parts = tag.Split(',');
            SettingsService.Current.PanelWidth  = double.Parse(parts[0]);
            SettingsService.Current.PanelHeight = double.Parse(parts[1]);
            SettingsService.Save();
        }
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        bool on = ChkStartup.IsChecked == true;
        SettingsService.Current.RunAtStartup = on;
        SettingsService.Save();
        StartupService.SetRunAtStartup(on);
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
