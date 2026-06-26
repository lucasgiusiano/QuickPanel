using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using QuickPanel.Core;
using QuickPanel.Models;
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

        Deactivated += OnDeactivated;
    }

    /// <summary>
    /// Cierra la ventana al hacer clic afuera, salvo que el foco se haya ido a un
    /// modal hijo (Administrar apps, capturador de hotkey, selector de archivo, etc.).
    /// </summary>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Si otra ventana de la propia app quedó activa, es un modal hijo: no cerrar.
        foreach (Window w in Application.Current.Windows)
            if (w != this && w.IsActive)
                return;

        // Diferir: si en el próximo ciclo seguimos sin foco y sin modal, cerrar.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsActive) return; // recuperó el foco
            foreach (Window w in Application.Current.Windows)
                if (w != this && w.IsActive)
                    return;       // un modal hijo tomó el foco
            Close();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void BuildSwatches()
    {
        foreach (var hex in SeedPalette)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);

            var dot = new Ellipse
            {
                Width           = 34,
                Height          = 34,
                Margin          = new Thickness(0),
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
                ThemeService.Apply(hex, SettingsService.Current.ThemeMode);
                foreach (var child in Swatches.Children.OfType<Grid>().Select(g => g.Children.OfType<Ellipse>().First()))
                    child.Stroke = Brushes.Transparent;
                dot.Stroke = (Brush)FindResource("Md3OnSurface");
            };

            var cell = new Grid { Margin = new Thickness(4) };
            cell.Children.Add(dot);
            Swatches.Children.Add(cell);
        }
    }

    // ── Selector de tamaño del panel de apps (ícono: ventana) ──

    private RadioButton? _sizeS, _sizeM, _sizeL;

    private void BuildPanelSizeRow()
    {
        PanelSizeRow.Children.Clear();
        _sizeS = SizeOption(PanelSizeRow, "S", "520,900",  WindowGlyph(0.55), Size_Click);
        _sizeM = SizeOption(PanelSizeRow, "M", "1040,1400", WindowGlyph(0.78), Size_Click);
        _sizeL = SizeOption(PanelSizeRow, "L", "1560,2000", WindowGlyph(1.00), Size_Click);
    }

    // ── Selector de tamaño del menú flotante (ícono: columna de círculos) ──

    private RadioButton? _menuS, _menuM, _menuL;

    private void BuildMenuSizeRow()
    {
        MenuSizeRow.Children.Clear();
        _menuS = SizeOption(MenuSizeRow, "S", "40", DotsGlyph(0.7),  MenuSize_Click);
        _menuM = SizeOption(MenuSizeRow, "M", "48", DotsGlyph(0.85), MenuSize_Click);
        _menuL = SizeOption(MenuSizeRow, "L", "58", DotsGlyph(1.0),  MenuSize_Click);
    }

    /// <summary>
    /// Tarjeta de opción de tamaño: ícono representativo arriba, letra S/M/L abajo,
    /// con badge de plan superpuesto si corresponde.
    /// </summary>
    private RadioButton SizeOption(Panel host, string label, string tag, FrameworkElement glyph,
        RoutedEventHandler onClick)
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

        rb.Content = content;

        rb.Click += onClick;
        host.Children.Add(rb);
        return rb;
    }

    /// <summary>Ícono de "ventana": representa el panel donde se abren las apps.</summary>
    private FrameworkElement WindowGlyph(double scale)
    {
        // Ventana estilizada: marco con barra de título integrada arriba.
        double w = Math.Round(20 * scale), h = Math.Round(24 * scale);

        var bar = new Border
        {
            Height       = Math.Max(3, Math.Round(h * 0.22)),
            Background    = (Brush)FindResource("Md3OnSurfaceVariant"),
            VerticalAlignment = VerticalAlignment.Top
        };

        var inner = new Grid();
        inner.Children.Add(bar);

        var border = new Border
        {
            Width           = w,
            Height          = h,
            CornerRadius    = new CornerRadius(4),
            Background      = (Brush)FindResource("Md3SurfaceContainerHigh"),
            BorderBrush     = (Brush)FindResource("Md3OnSurfaceVariant"),
            BorderThickness = new Thickness(1.4),
            ClipToBounds    = true,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child           = inner
        };
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
            <= 520  => _sizeS,
            >= 1560 => _sizeL,
            _       => _sizeM
        })!.IsChecked = true;

        (s.MenuItemSize switch
        {
            <= 40 => _menuS,
            >= 58 => _menuL,
            _     => _menuM
        })!.IsChecked = true;

        ChkStartup.IsChecked = s.RunAtStartup;
        ChkAutoHide.IsChecked = s.AutoHide;
        ChkBadges.IsChecked = s.ShowBadges;
        ChkLite.IsChecked = s.LiteMode;
        PopulateStartAppCombo();
        BuildActionHotkeys();

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

    private void MenuSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;

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
        SettingsService.Current.AutoHide = ChkAutoHide.IsChecked == true;
        SettingsService.Save();
    }

    private void Badges_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.ShowBadges = ChkBadges.IsChecked == true;
        SettingsService.Save();
    }

    private void Lite_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.LiteMode = ChkLite.IsChecked == true;
        SettingsService.Save();
        // Algunos efectos (autofill, environment) aplican a paneles nuevos; otros
        // (suspensión, memoria, límite) aplican en caliente al ocultar/abrir.
    }

    private static readonly (HotkeyAction action, string label)[] ActionList =
    {
        (HotkeyAction.ToggleMenu,      "Abrir/cerrar menú"),
        (HotkeyAction.HideActivePanel, "Ocultar panel activo"),
        (HotkeyAction.NextApp,         "App siguiente"),
        (HotkeyAction.PrevApp,         "App anterior"),
        (HotkeyAction.ToggleAutoHide,  "Activar/desactivar auto-ocultar"),
        (HotkeyAction.MoveButton,      "Mover el botón flotante"),
    };

    private void BuildActionHotkeys()
    {
        ActionHotkeysRow.Children.Clear();
        foreach (var (action, label) in ActionList)
        {
            var key = action.ToString();
            SettingsService.Current.ActionHotkeys.TryGetValue(key, out var hk);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = label, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Md3OnSurface")
            });

            var btn = new Button
            {
                Content = hk?.IsSet == true ? hk.ToString() : "Asignar",
                Style   = (Style)FindResource("Md3TextButton")
            };
            Grid.SetColumn(btn, 1);
            btn.Click += (_, _) => AssignActionHotkey(action, label);
            row.Children.Add(btn);

            ActionHotkeysRow.Children.Add(row);
        }
    }

    private void AssignActionHotkey(HotkeyAction action, string label)
    {
        var dlg = new HotkeyCaptureDialog(label) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;

        var hk = dlg.Result;
        if (hk.IsSet && ActionHotkeyTaken(hk, action))
        {
            MessageBox.Show("Esa combinación ya está asignada a otra app o acción.",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SettingsService.Current.ActionHotkeys[action.ToString()] = hk;
        SettingsService.Save();
        App.ReloadHotkeys();
        BuildActionHotkeys();
    }

    private static bool ActionHotkeyTaken(Hotkey hk, HotkeyAction except)
    {
        string s = hk.ToString();
        if (SettingsService.Current.Apps.Any(a => a.Hotkey.IsSet && a.Hotkey.ToString() == s))
            return true;
        return SettingsService.Current.ActionHotkeys
            .Any(kv => kv.Key != except.ToString() && kv.Value != null && kv.Value.IsSet && kv.Value.ToString() == s);
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
        var tag = (StartAppCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "";
        SettingsService.Current.StartAppId = tag;
        SettingsService.Save();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        var mode = Enum.Parse<ThemeMode>(tag);

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

    private void Export_Click(object sender, RoutedEventArgs e)
    {
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
