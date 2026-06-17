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

    private const int FreePaletteCount = 4;

    private void BuildSwatches()
    {
        for (int i = 0; i < SeedPalette.Length; i++)
        {
            var hex      = SeedPalette[i];
            bool premium = i >= FreePaletteCount;
            var c        = (Color)ColorConverter.ConvertFromString(hex);

            var dot = new Ellipse
            {
                Width           = 34,
                Height          = 34,
                Margin          = new Thickness(4),
                Fill            = new SolidColorBrush(c),
                Cursor          = Cursors.Hand,
                Stroke          = Brushes.Transparent,
                StrokeThickness = 3,
                // Las premium se ven atenuadas hasta desbloquear el plan.
                Opacity         = premium && !LicenseService.HasFeature(Feature.PremiumPalettes) ? 0.4 : 1.0
            };

            if (string.Equals(hex, SettingsService.Current.SeedColor, StringComparison.OrdinalIgnoreCase))
                dot.Stroke = (Brush)FindResource("Md3OnSurface");

            dot.MouseLeftButtonUp += (_, _) =>
            {
                if (premium && !LicenseService.HasFeature(Feature.PremiumPalettes))
                {
                    new UpgradeWindow("Las paletas premium son parte del plan Pro.").ShowDialog();
                    return;
                }

                SettingsService.Current.SeedColor = hex;
                SettingsService.Save();
                ThemeService.Apply(hex, SettingsService.Current.ThemeMode);
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
        PlanText.Text = $"Plan actual: {LicenseService.Name(LicenseService.CurrentTier)}";

        (s.ThemeMode switch
        {
            ThemeMode.Light  => ThemeLight,
            ThemeMode.System => ThemeSystem,
            _                => ThemeDark
        }).IsChecked = true;
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

            // Aplicar en caliente a los paneles ya abiertos (antes había que
            // cerrarlos y reabrirlos para que tomara el nuevo tamaño).
            App.ReanchorAllPanels();
        }
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        bool on = ChkStartup.IsChecked == true;
        SettingsService.Current.RunAtStartup = on;
        SettingsService.Save();
        StartupService.SetRunAtStartup(on);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        var mode = Enum.Parse<ThemeMode>(tag);

        // Cualquier modo distinto de Oscuro es Pro.
        if (mode != ThemeMode.Dark && !LicenseService.HasFeature(Feature.LightTheme))
        {
            new UpgradeWindow("Los temas claro y sistema son parte del plan Pro.").ShowDialog();
            ThemeDark.IsChecked = true; // revertir a oscuro (gratis)
            return;
        }

        SettingsService.Current.ThemeMode = mode;
        SettingsService.Save();
        ThemeService.Apply(SettingsService.Current.SeedColor, mode);
    }

    private void Manage_Click(object sender, RoutedEventArgs e)
        => new ManageAppsWindow(_manager).ShowDialog();

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private void Plans_Click(object sender, RoutedEventArgs e)
    {
        new UpgradeWindow().ShowDialog();
        // Refrescar el texto por si cambió el plan
        PlanText.Text = $"Plan actual: {LicenseService.Name(LicenseService.CurrentTier)}";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!LicenseService.HasFeature(Feature.ImportExport))
        {
            new UpgradeWindow("Exportar e importar la configuración es parte del plan Complete.").ShowDialog();
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter   = "QuickPanel config (*.json)|*.json",
            FileName = "quickpanel-config.json"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                SettingsService.Export(dlg.FileName);
                MessageBox.Show("Configuración exportada.", "QuickPanel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show("No se pudo exportar la configuración.", "QuickPanel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!LicenseService.HasFeature(Feature.ImportExport))
        {
            new UpgradeWindow("Exportar e importar la configuración es parte del plan Complete.").ShowDialog();
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "QuickPanel config (*.json)|*.json"
        };
        if (dlg.ShowDialog() == true)
        {
            if (SettingsService.Import(dlg.FileName))
            {
                ThemeService.Apply(SettingsService.Current.SeedColor, SettingsService.Current.ThemeMode);
                App.ReanchorAllPanels();
                MessageBox.Show("Configuración importada.", "QuickPanel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            else
            {
                MessageBox.Show("El archivo no es una configuración válida.", "QuickPanel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
