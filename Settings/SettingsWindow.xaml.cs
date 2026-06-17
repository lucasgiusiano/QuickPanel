using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
        BuildPanelSizeRow();
        BuildMenuSizeRow();
        LoadState();

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"QuickPanel v{v?.Major}.{v?.Minor}.{v?.Build}";
    }

    private const int FreePaletteCount = 4;

    /// <summary>Badge pequeño (corona Pro / diamante Complete) para marcar opciones de pago.</summary>
    private FrameworkElement? PlanBadge(Feature feature)
    {
        if (LicenseService.HasFeature(feature)) return null;

        var tier = LicenseService.MinTierFor(feature);
        var (glyph, brushKey) = tier == LicenseTier.Complete ? ("◆", "Md3Error") : ("♛", "Md3Primary");

        return new TextBlock
        {
            Text       = glyph,
            FontSize   = 10,
            Margin     = new Thickness(0, -4, -4, 0),
            Foreground = (Brush)FindResource(brushKey),
            ToolTip    = $"Requiere plan {LicenseService.Name(tier)}"
        };
    }

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
                foreach (var child in Swatches.Children.OfType<Grid>().Select(g => g.Children.OfType<Ellipse>().First()))
                    child.Stroke = Brushes.Transparent;
                dot.Stroke = (Brush)FindResource("Md3OnSurface");
            };

            var cell = new Grid { Margin = new Thickness(4) };
            dot.Margin = new Thickness(0);
            cell.Children.Add(dot);

            if (premium && !LicenseService.HasFeature(Feature.PremiumPalettes))
            {
                cell.ToolTip = "Requiere plan Pro";
                cell.Children.Add(new TextBlock
                {
                    Text       = "♛",
                    FontSize   = 11,
                    IsHitTestVisible    = false,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment   = VerticalAlignment.Top,
                    Margin     = new Thickness(0, -2, -2, 0),
                    Foreground = (Brush)FindResource("Md3Primary"),
                    Effect     = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9, Color = Colors.Black }
                });
            }

            Swatches.Children.Add(cell);
        }
    }

    // ── Selector de tamaño del panel de apps (ícono: ventana) ──

    private RadioButton? _sizeS, _sizeM, _sizeL;

    private void BuildPanelSizeRow()
    {
        PanelSizeRow.Children.Clear();
        _sizeS = SizeOption(PanelSizeRow, "S", "360,600", WindowGlyph(0.55), Size_Click, null);
        _sizeM = SizeOption(PanelSizeRow, "M", "440,760", WindowGlyph(0.75), Size_Click, null);
        _sizeL = SizeOption(PanelSizeRow, "L", "520,900", WindowGlyph(1.00), Size_Click, null);
    }

    // ── Selector de tamaño del menú flotante (ícono: columna de círculos) ──

    private RadioButton? _menuS, _menuM, _menuL;

    private void BuildMenuSizeRow()
    {
        MenuSizeRow.Children.Clear();
        _menuS = SizeOption(MenuSizeRow, "S", "40", DotsGlyph(0.7),  MenuSize_Click, PlanBadge(Feature.MenuButtonSize));
        _menuM = SizeOption(MenuSizeRow, "M", "48", DotsGlyph(0.85), MenuSize_Click, PlanBadge(Feature.MenuButtonSize));
        _menuL = SizeOption(MenuSizeRow, "L", "58", DotsGlyph(1.0),  MenuSize_Click, PlanBadge(Feature.MenuButtonSize));
    }

    /// <summary>
    /// Tarjeta de opción de tamaño: ícono representativo arriba, letra S/M/L abajo,
    /// con badge de plan superpuesto si corresponde.
    /// </summary>
    private RadioButton SizeOption(Panel host, string label, string tag, FrameworkElement glyph,
        RoutedEventHandler onClick, FrameworkElement? badge)
    {
        var rb = new RadioButton
        {
            GroupName = host == PanelSizeRow ? "panelsize" : "menusize",
            Tag       = tag,
            Margin    = new Thickness(0, 0, 10, 0),
            Style     = (Style)FindResource("SizeCardRadio")
        };

        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var glyphHost = new Grid { Width = 40, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
        glyphHost.Children.Add(glyph);
        content.Children.Add(glyphHost);
        content.Children.Add(new TextBlock
        {
            Text                = label,
            FontSize            = 11,
            FontWeight          = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 4, 0, 0),
            Foreground          = (Brush)FindResource("Md3OnSurfaceVariant")
        });

        if (badge != null)
        {
            var wrap = new Grid();
            wrap.Children.Add(content);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment   = VerticalAlignment.Top;
            badge.IsHitTestVisible    = false;
            wrap.Children.Add(badge);
            rb.Content = wrap;
            rb.ToolTip = badge.ToolTip;
        }
        else
        {
            rb.Content = content;
        }

        rb.Click += onClick;
        host.Children.Add(rb);
        return rb;
    }

    /// <summary>Ícono de "ventana": representa el panel donde se abren las apps.</summary>
    private FrameworkElement WindowGlyph(double scale)
    {
        double w = 22 * scale, h = 26 * scale;
        var border = new Border
        {
            Width           = w,
            Height          = h,
            CornerRadius    = new CornerRadius(3),
            Background      = (Brush)FindResource("Md3SurfaceContainerHigh"),
            BorderBrush     = (Brush)FindResource("Md3OnSurfaceVariant"),
            BorderThickness = new Thickness(1.2),
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var bar = new Border
        {
            Height              = Math.Max(3, h * 0.18),
            Background          = (Brush)FindResource("Md3OnSurfaceVariant"),
            VerticalAlignment   = VerticalAlignment.Top,
            CornerRadius        = new CornerRadius(3, 3, 0, 0)
        };
        var grid = new Grid();
        grid.Children.Add(bar);
        border.Child = grid;
        return border;
    }

    /// <summary>Ícono de "columna de círculos": representa los ítems del menú flotante.</summary>
    private FrameworkElement DotsGlyph(double scale)
    {
        var sp = new StackPanel
        {
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        for (int i = 0; i < 3; i++)
        {
            double d = (8 + i * 2) * scale; // crecen levemente hacia abajo, como en el menú real
            sp.Children.Add(new Ellipse
            {
                Width      = d,
                Height     = d,
                Margin     = new Thickness(0, 1.5, 0, 1.5),
                Fill       = (Brush)FindResource(i == 1 ? "Md3Primary" : "Md3OnSurfaceVariant")
            });
        }
        return sp;
    }

    private void LoadState()
    {
        var s = SettingsService.Current;
        (s.PanelWidth switch
        {
            <= 360 => _sizeS,
            >= 520 => _sizeL,
            _      => _sizeM
        })!.IsChecked = true;

        (s.MenuItemSize switch
        {
            <= 40 => _menuS,
            >= 58 => _menuL,
            _     => _menuM
        })!.IsChecked = true;

        ChkStartup.IsChecked = s.RunAtStartup;
        ChkAutoHide.IsChecked = s.AutoHide;
        PopulateStartAppCombo();
        PlanText.Text = $"Plan actual: {LicenseService.Name(LicenseService.CurrentTier)}";

        (s.ThemeMode switch
        {
            ThemeMode.Light  => ThemeLight,
            ThemeMode.System => ThemeSystem,
            _                => ThemeDark
        }).IsChecked = true;

        // Marcar con corona las opciones que requieren Pro.
        if (!LicenseService.HasFeature(Feature.LightTheme))
        {
            ThemeLight.Content  = "Claro ♛";
            ThemeLight.ToolTip  = "Requiere plan Pro";
            ThemeSystem.Content = "Sistema ♛";
            ThemeSystem.ToolTip = "Requiere plan Pro";
        }
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

    private void MenuSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;

        if (!LicenseService.HasFeature(Feature.MenuButtonSize))
        {
            new UpgradeWindow("Cambiar el tamaño del menú flotante es parte del plan Complete.").ShowDialog();
            // Revertir a la selección previa (la M por defecto si no había ninguna).
            (SettingsService.Current.MenuItemSize switch
            {
                <= 40 => _menuS,
                >= 58 => _menuL,
                _     => _menuM
            })!.IsChecked = true;
            return;
        }

        SettingsService.Current.MenuItemSize = double.Parse(tag);
        SettingsService.Save();
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        bool on = ChkStartup.IsChecked == true;
        SettingsService.Current.RunAtStartup = on;
        SettingsService.Save();
        StartupService.SetRunAtStartup(on);
    }

    private void AutoHide_Click(object sender, RoutedEventArgs e)
    {
        if (ChkAutoHide.IsChecked == true && !LicenseService.HasFeature(Feature.AutoHide))
        {
            new UpgradeWindow("Auto-ocultar el botón es parte del plan Pro.").ShowDialog();
            ChkAutoHide.IsChecked = false;
            return;
        }
        SettingsService.Current.AutoHide = ChkAutoHide.IsChecked == true;
        SettingsService.Save();
    }

    private bool _populatingCombo;

    private void PopulateStartAppCombo()
    {
        _populatingCombo = true;
        StartAppCombo.Items.Clear();
        StartAppCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Ninguna", Tag = "" });
        foreach (var app in SettingsService.Current.Apps)
            StartAppCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = app.Name, Tag = app.Id });

        var current = SettingsService.Current.StartAppId;
        StartAppCombo.SelectedIndex = 0;
        for (int i = 1; i < StartAppCombo.Items.Count; i++)
            if (((System.Windows.Controls.ComboBoxItem)StartAppCombo.Items[i]).Tag as string == current)
                StartAppCombo.SelectedIndex = i;
        _populatingCombo = false;
    }

    private void StartApp_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_populatingCombo) return;
        if (!LicenseService.HasFeature(Feature.StartApp))
        {
            new UpgradeWindow("Abrir una app al iniciar es parte del plan Pro.").ShowDialog();
            _populatingCombo = true;
            StartAppCombo.SelectedIndex = 0;
            _populatingCombo = false;
            return;
        }
        var tag = (StartAppCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "";
        SettingsService.Current.StartAppId = tag;
        SettingsService.Save();
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
