using System.Windows;
using System.Linq;
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

    /// <summary>
    /// Badge pequeño (corona Pro / diamante Complete) para marcar opciones de pago.
    /// Tooltip explica el plan requerido al pasar el mouse.
    /// </summary>
    private FrameworkElement? PlanBadge(Feature feature)
    {
        if (LicenseService.HasFeature(feature)) return null; // ya desbloqueado: sin badge

        var tier = LicenseService.MinTierFor(feature);
        var (glyph, brushKey) = tier == LicenseTier.Complete ? ("◆", "Md3Error") : ("♛", "Md3Primary");

        var badge = new TextBlock
        {
            Text       = glyph,
            FontSize   = 10,
            Margin     = new Thickness(2, -8, -2, 0),
            Foreground = (Brush)FindResource(brushKey),
            ToolTip    = $"Requiere plan {LicenseService.Name(tier)}",
            VerticalAlignment = VerticalAlignment.Top
        };
        return badge;
    }

    private void BuildRows()
    {
        Rows.Children.Clear();
        var all = SettingsService.Current.Apps;

        string q = SearchBox?.Text?.Trim() ?? "";
        var apps = string.IsNullOrEmpty(q)
            ? all
            : all.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                          || a.Url.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        if (apps.Count == 0)
        {
            Rows.Children.Add(new TextBlock
            {
                Text       = string.IsNullOrEmpty(q) ? "No agregaste apps todavía." : "Sin resultados.",
                FontSize   = 13,
                Margin     = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)FindResource("Md3OnSurfaceVariant")
            });
            return;
        }

        for (int i = 0; i < apps.Count; i++)
            Rows.Children.Add(BuildRow(apps[i], i, apps.Count));
    }

    private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => BuildRows();

    private Border BuildRow(AppEntry app, int index, int total)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Margin       = new Thickness(0, 0, 0, 8),
            Padding      = new Thickness(10),
            Background   = (Brush)FindResource("Md3SurfaceContainer"),
            Tag          = app,                 // usado por el drag&drop para identificar la fila
            AllowDrop    = true,
            Cursor       = Cursors.SizeAll
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // handle
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // ícono
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // nombre
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // acciones

        // ── Handle de arrastre ──
        var handle = new TextBlock
        {
            Text              = "⠿",
            FontSize          = 16,
            Margin            = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = (Brush)FindResource("Md3OnSurfaceVariant"),
            Cursor            = Cursors.SizeAll
        };
        Grid.SetColumn(handle, 0);
        grid.Children.Add(handle);

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
        Grid.SetColumn(icon, 1);
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
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);

        // ── Acciones ──
        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

        actions.Children.Add(IconButton("⌨", HotkeyTip(app), PlanBadge(Feature.GlobalHotkeys), () => AssignHotkey(app)));
        actions.Children.Add(IconButton("🖼", "Ícono personalizado", PlanBadge(Feature.CustomIcons), () => PickIcon(app)));
        actions.Children.Add(IconButton("🎨", "Color de la app", PlanBadge(Feature.PerAppColor), b => PickColor(app, b)));
        actions.Children.Add(IconButton("✕", "Quitar", (FrameworkElement?)null, () => Remove(app)));

        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        card.Child = grid;
        WireDragDrop(card);
        return card;
    }

    // ── Drag & drop de filas ──

    private Border? _dragSource;

    private void WireDragDrop(Border row)
    {
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // Si el clic empezó en un control interactivo (texto, botón), no iniciar drag.
            if (e.OriginalSource is TextBox or Button or TextBlock { Text.Length: > 1 }) return;

            _dragSource = row;
            DragDrop.DoDragDrop(row, row, DragDropEffects.Move);
        };

        row.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(typeof(Border)) ? DragDropEffects.Move : DragDropEffects.None;
            row.Opacity = 0.7;
            e.Handled = true;
        };

        row.DragLeave += (_, _) => row.Opacity = 1.0;

        row.Drop += (_, e) =>
        {
            row.Opacity = 1.0;
            if (_dragSource == null || _dragSource == row) return;
            if (_dragSource.Tag is not AppEntry from || row.Tag is not AppEntry to) return;

            var apps = SettingsService.Current.Apps;
            int iFrom = apps.IndexOf(from);
            int iTo   = apps.IndexOf(to);
            if (iFrom < 0 || iTo < 0) return;

            apps.RemoveAt(iFrom);
            apps.Insert(iTo, from);
            SettingsService.Save();
            BuildRows();
        };
    }

    // ── Acciones ──

    private static string HotkeyTip(AppEntry app)
        => app.Hotkey.IsSet ? $"Atajo: {app.Hotkey}" : "Asignar atajo de teclado";

    private void AssignHotkey(AppEntry app)
    {
        if (!LicenseService.HasFeature(Feature.GlobalHotkeys))
        {
            new UpgradeWindow("Los atajos de teclado globales son parte del plan Complete.").ShowDialog();
            return;
        }

        var dlg = new HotkeyCaptureDialog(app.Name) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;

        var hk = dlg.Result;

        // Evitar duplicados: si otra app o acción ya usa esta combinación, avisar.
        if (hk.IsSet && IsHotkeyTaken(hk, app))
        {
            MessageBox.Show("Esa combinación ya está asignada a otra app o acción.",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        app.Hotkey = hk;
        SettingsService.Save();
        App.ReloadHotkeys();
        BuildRows();
    }

    private static bool IsHotkeyTaken(Hotkey hk, AppEntry except)
    {
        string s = hk.ToString();
        if (SettingsService.Current.Apps.Any(a => a.Id != except.Id && a.Hotkey.IsSet && a.Hotkey.ToString() == s))
            return true;
        return SettingsService.Current.ActionHotkeys.Values.Any(h => h != null && h.IsSet && h.ToString() == s);
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
            AutoAssignHotkey(dlg.Result);
            SettingsService.Current.Apps.Add(dlg.Result);
            SettingsService.Save();
            App.ReloadHotkeys();
            BuildRows();
        }
    }

    /// <summary>
    /// Asigna Ctrl+Alt+[1..0] a las primeras 10 apps automáticamente.
    /// La 10ª usa la tecla 0; de la 11ª en adelante no se asigna nada.
    /// </summary>
    private static void AutoAssignHotkey(AppEntry app)
    {
        int pos = SettingsService.Current.Apps.Count; // índice que tendrá la nueva app
        if (pos > 9) return; // ya hay 10 (0..9): sin atajo automático

        var key = pos < 9
            ? Key.D1 + pos       // D1..D9 para posiciones 0..8
            : Key.D0;            // posición 9 (10ª app) -> 0

        var hk = new Hotkey { Ctrl = true, Alt = true, Key = key };
        if (!IsHotkeyTaken(hk, app))
            app.Hotkey = hk;
    }

    // ── Helpers ──

    private Grid IconButton(string glyph, string tip, FrameworkElement? badge, Action onClick)
        => IconButton(glyph, tip, badge, _ => onClick());

    /// <summary>Botón de ícono con badge de plan superpuesto (esquina sup. derecha) si corresponde.</summary>
    private Grid IconButton(string glyph, string tip, FrameworkElement? badge, Action<UIElement> onClick)
    {
        var btn = new Button
        {
            Content = glyph,
            ToolTip = badge?.ToolTip ?? tip,
            Style   = (Style)FindResource("TitleBtn")
        };
        btn.Click += (s, _) => onClick((UIElement)s);

        var wrap = new Grid();
        wrap.Children.Add(btn);
        if (badge != null)
        {
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.IsHitTestVisible    = false; // el click pasa al botón de abajo
            wrap.Children.Add(badge);
        }
        return wrap;
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
