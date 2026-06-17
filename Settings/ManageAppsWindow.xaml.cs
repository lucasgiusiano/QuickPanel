using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickPanel.Core;
using QuickPanel.Models;
using QuickPanel.Services;

namespace QuickPanel.Settings;

public partial class ManageAppsWindow : Window
{
    private readonly OverlayManager? _manager;

    private static readonly string[] AccentPalette =
    {
        "#6366F1", "#8B5CF6", "#EC4899", "#EF4444", "#F59E0B",
        "#10B981", "#06B6D4", "#3B82F6", "#64748B", "#14B8A6"
    };

    public ManageAppsWindow(OverlayManager? manager)
    {
        _manager = manager;
        InitializeComponent();
        BuildRows();
    }

    private void BuildRows()
    {
        Rows.Children.Clear();
        var apps = SettingsService.Current.Apps;

        if (apps.Count == 0)
        {
            Rows.Children.Add(new TextBlock
            {
                Text       = "No agregaste apps todavía.",
                FontSize   = 13,
                Margin     = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)FindResource("Md3OnSurfaceVariant")
            });
            return;
        }

        for (int i = 0; i < apps.Count; i++)
            Rows.Children.Add(BuildRow(apps[i], i, apps.Count));
    }

    private Border BuildRow(AppEntry app, int index, int total)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Margin       = new Thickness(0, 0, 0, 8),
            Padding      = new Thickness(10),
            Background   = (Brush)FindResource("Md3SurfaceContainer")
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });            // ícono
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // nombre
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });            // acciones

        // ── Ícono ──
        var icon = new Border
        {
            Width        = 32,
            Height       = 32,
            CornerRadius = new CornerRadius(16),
            Margin       = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background   = AccentBrush(app) ?? (Brush)FindResource("Md3SurfaceContainerHigh")
        };
        var img = LoadIcon(app);
        if (img != null)
            icon.Child = new Image { Source = img, Width = 20, Height = 20,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        // ── Nombre editable ──
        var name = new TextBox
        {
            Text              = app.Name,
            Style             = (Style)FindResource("Md3TextBox"),
            VerticalAlignment = VerticalAlignment.Center
        };
        name.LostFocus += (_, _) =>
        {
            var t = name.Text.Trim();
            if (!string.IsNullOrEmpty(t) && t != app.Name)
            {
                app.Name = t;
                SettingsService.Save();
            }
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        // ── Acciones ──
        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

        actions.Children.Add(IconButton("▲", "Subir", index > 0, () => Move(index, -1)));
        actions.Children.Add(IconButton("▼", "Bajar", index < total - 1, () => Move(index, +1)));
        actions.Children.Add(IconButton("🖼", "Ícono", true, () => PickIcon(app)));
        actions.Children.Add(IconButton("🎨", "Color", true, b => PickColor(app, b)));
        actions.Children.Add(IconButton("✕", "Quitar", true, () => Remove(app)));

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    // ── Acciones ──

    private void Move(int index, int delta)
    {
        var apps = SettingsService.Current.Apps;
        int j = index + delta;
        if (j < 0 || j >= apps.Count) return;
        (apps[index], apps[j]) = (apps[j], apps[index]);
        SettingsService.Save();
        BuildRows();
    }

    private void PickIcon(AppEntry app)
    {
        if (!LicenseService.HasFeature(Feature.CustomIcons))
        {
            new UpgradeWindow("Los íconos personalizados son parte del plan Pro.").ShowDialog();
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico"
        };
        if (dlg.ShowDialog() == true)
        {
            app.IconPath = dlg.FileName;
            SettingsService.Save();
            BuildRows();
        }
    }

    private void PickColor(AppEntry app, UIElement anchor)
    {
        if (!LicenseService.HasFeature(Feature.PerAppColor))
        {
            new UpgradeWindow("El color por app es parte del plan Complete.").ShowDialog();
            return;
        }

        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement       = PlacementMode.Bottom,
            StaysOpen       = false,
            AllowsTransparency = true
        };

        var wrap = new WrapPanel { Width = 168 };
        var box = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding      = new Thickness(8),
            Background   = (Brush)FindResource("Md3SurfaceContainerHigh"),
            BorderBrush  = (Brush)FindResource("Md3Outline"),
            BorderThickness = new Thickness(1),
            Child        = wrap
        };

        void AddSwatch(string? hex)
        {
            var dot = new Border
            {
                Width        = 26,
                Height       = 26,
                Margin       = new Thickness(3),
                CornerRadius = new CornerRadius(13),
                Cursor       = Cursors.Hand,
                Background   = hex == null
                    ? (Brush)FindResource("Md3SurfaceContainer")
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderBrush     = (Brush)FindResource("Md3Outline"),
                BorderThickness = new Thickness(hex == null ? 1 : 0)
            };
            if (hex == null)
                dot.Child = new TextBlock { Text = "∅", FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("Md3OnSurfaceVariant") };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                app.Color = hex ?? "";
                SettingsService.Save();
                popup.IsOpen = false;
                BuildRows();
            };
            wrap.Children.Add(dot);
        }

        AddSwatch(null); // sin color
        foreach (var hex in AccentPalette) AddSwatch(hex);

        popup.Child = box;
        popup.IsOpen = true;
    }

    private void Remove(AppEntry app)
    {
        if (MessageBox.Show($"¿Quitar {app.Name}?", "QuickPanel",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (_manager != null) _manager.RemoveApp(app);
        else
        {
            SettingsService.Current.Apps.RemoveAll(a => a.Id == app.Id);
            SettingsService.Save();
        }
        BuildRows();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!LicenseService.CanAddApp(SettingsService.Current.Apps.Count))
        {
            new UpgradeWindow(
                $"Llegaste al límite de {LicenseService.FreeAppLimit} apps del plan Free.").ShowDialog();
            if (!LicenseService.CanAddApp(SettingsService.Current.Apps.Count)) return;
        }

        var dlg = new AddAppDialog();
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            SettingsService.Current.Apps.Add(dlg.Result);
            SettingsService.Save();
            BuildRows();
        }
    }

    // ── Helpers ──

    private Button IconButton(string glyph, string tip, bool enabled, Action onClick)
        => IconButton(glyph, tip, enabled, _ => onClick());

    private Button IconButton(string glyph, string tip, bool enabled, Action<UIElement> onClick)
    {
        var btn = new Button
        {
            Content   = glyph,
            ToolTip   = tip,
            IsEnabled = enabled,
            Style     = (Style)FindResource("TitleBtn")
        };
        btn.Click += (s, _) => onClick((UIElement)s);
        return btn;
    }

    private Brush? AccentBrush(AppEntry app)
    {
        if (string.IsNullOrEmpty(app.Color)) return null;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(app.Color)); }
        catch { return null; }
    }

    private static ImageSource? LoadIcon(AppEntry app)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = app.HasCustomIcon
                ? new Uri(app.IconPath)
                : new Uri(AppEntry.FaviconFor(app.Url));
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
