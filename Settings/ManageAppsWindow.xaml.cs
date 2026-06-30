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
                Text       = string.IsNullOrEmpty(q) ? Loc.T("Manage_NoApps") : Loc.T("Manage_NoResults"),
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
            AllowDrop    = true
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
        WireDragHandle(handle, card);

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

        actions.Children.Add(IconButton("⌨", HotkeyTip(app), () => AssignHotkey(app)));
        actions.Children.Add(IconButton("📁", GroupTip(app), b => PickGroup(app, b)));
        actions.Children.Add(IconButton("🖼", Loc.T("Manage_CustomIcon"), () => PickIcon(app)));
        actions.Children.Add(IconButton("🎨", Loc.T("Manage_AppColor"), b => PickColor(app, b)));
        actions.Children.Add(IconButton("✕", Loc.T("Common_Remove"), () => Remove(app)));

        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        card.Child = grid;
        WireDropTarget(card);
        return card;
    }

    // ── Drag & drop de filas ──

    private Border? _dragSource;

    /// <summary>El arrastre se inicia SOLO desde el handle (⠿), no desde toda la fila,
    /// para no interferir con los botones de acción de la derecha.</summary>
    private void WireDragHandle(UIElement handle, Border row)
    {
        handle.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragSource = row;
            DragDrop.DoDragDrop(row, row, DragDropEffects.Move);
            e.Handled = true;
        };
    }

    private void WireDropTarget(Border row)
    {
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
            ReassignPositionalHotkeys();
            SettingsService.Save();
            App.ReloadHotkeys();
            BuildRows();
        };
    }

    /// <summary>
    /// Reasigna Ctrl+Alt+[1..0] a las primeras 10 apps según su nueva posición,
    /// pero solo a las que ya tenían un atajo posicional (Ctrl+Alt+dígito).
    /// Los atajos personalizados por el usuario se respetan y no se tocan.
    /// </summary>
    private static void ReassignPositionalHotkeys()
    {
        var apps = SettingsService.Current.Apps;

        // Detectar qué apps usan un atajo "posicional" (Ctrl+Alt+ y un dígito).
        bool IsPositional(Hotkey h) =>
            h.IsSet && h.Ctrl && h.Alt && !h.Shift && !h.Win && IsDigit(h.Key);

        // Liberar los atajos posicionales actuales para reasignarlos por orden.
        foreach (var a in apps)
            if (IsPositional(a.Hotkey)) a.Hotkey = new Hotkey();

        for (int i = 0; i < apps.Count && i < 10; i++)
        {
            // Solo asignar si esa posición no quedó con un atajo custom ya puesto.
            if (apps[i].Hotkey.IsSet) continue;
            var key = i < 9 ? Key.D1 + i : Key.D0;
            var hk  = new Hotkey { Ctrl = true, Alt = true, Key = key };
            if (!apps.Any(a => a.Hotkey.IsSet && a.Hotkey.ToString() == hk.ToString()))
                apps[i].Hotkey = hk;
        }
    }

    private static bool IsDigit(Key k) =>
        (k >= Key.D0 && k <= Key.D9) || (k >= Key.NumPad0 && k <= Key.NumPad9);

    // ── Acciones ──

    private static string HotkeyTip(AppEntry app)
        => app.Hotkey.IsSet ? string.Format(Loc.T("Manage_HotkeyTipSet"), app.Hotkey) : Loc.T("Manage_HotkeyTipUnset");

    private void AssignHotkey(AppEntry app)
    {
        HotkeyCaptureDialog dlg;
        try
        {
            dlg = new HotkeyCaptureDialog(app.Name) { Owner = this };
        }
        catch
        {
            dlg = new HotkeyCaptureDialog(app.Name); // sin owner si algo falla
        }
        if (dlg.ShowDialog() != true || dlg.Result == null) return;

        var hk = dlg.Result;

        // Evitar duplicados: si otra app o acción ya usa esta combinación, avisar.
        if (hk.IsSet && IsHotkeyTaken(hk, app))
        {
            MessageBox.Show(Loc.T("Common_DuplicateHotkey"),
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

    private void Groups_Click(object sender, RoutedEventArgs e) => ManageGroups();

    private static string GroupTip(AppEntry app)
    {
        var g = SettingsService.Current.Groups.FirstOrDefault(x => x.Id == app.GroupId);
        return g != null ? string.Format(Loc.T("Manage_FolderColon"), g.Name) : Loc.T("Manage_AssignFolder");
    }

    private void PickGroup(AppEntry app, UIElement anchor)
    {
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement       = PlacementMode.Bottom,
            StaysOpen       = false,
            AllowsTransparency = true
        };

        var panel = new StackPanel { Width = 200 };
        var box = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding      = new Thickness(6),
            Background   = (Brush)FindResource("Md3SurfaceContainerHigh"),
            BorderBrush  = (Brush)FindResource("Md3Outline"),
            BorderThickness = new Thickness(1),
            Child        = panel
        };

        Button Row(string text, Action onClick, bool selected = false)
        {
            var b = new Button
            {
                Content = selected ? "• " + text : text,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Style = (Style)FindResource("Md3TextButton"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            b.Click += (_, _) => { popup.IsOpen = false; onClick(); };
            return b;
        }

        panel.Children.Add(Row(Loc.T("Manage_NoFolder"), () => { app.GroupId = ""; SettingsService.Save(); BuildRows(); },
            string.IsNullOrEmpty(app.GroupId)));

        foreach (var g in SettingsService.Current.Groups)
        {
            var gid = g.Id;
            panel.Children.Add(Row(g.Name, () => { app.GroupId = gid; SettingsService.Save(); BuildRows(); },
                app.GroupId == gid));
        }

        var sep = new Border { Height = 1, Margin = new Thickness(4, 4, 4, 4),
            Background = (Brush)FindResource("Md3Outline") };
        panel.Children.Add(sep);
        panel.Children.Add(Row("＋ " + Loc.T("Manage_NewFolder"), () => CreateGroupAndAssign(app)));

        popup.Child = box;
        popup.IsOpen = true;
    }

    private void CreateGroupAndAssign(AppEntry app)
    {
        var name = PromptText(Loc.T("Manage_FolderNamePrompt"), Loc.T("Manage_FolderDefault"));
        if (string.IsNullOrWhiteSpace(name)) return;

        var group = new AppGroup { Name = name.Trim() };
        SettingsService.Current.Groups.Add(group);
        app.GroupId = group.Id;
        SettingsService.Save();
        BuildRows();
    }

    /// <summary>Mini-diálogo de texto reutilizable (sin dependencias externas).</summary>
    private string? PromptText(string title, string initial)
    {
        var win = new Window
        {
            Title = title, Width = 320, Height = 160,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = null,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var tb = new TextBox { Text = initial, Style = (Style)FindResource("Md3TextBox"), Margin = new Thickness(0,0,0,12) };
        var ok = new Button { Content = Loc.T("Common_OK"), Style = (Style)FindResource("Md3FilledButton"), HorizontalAlignment = HorizontalAlignment.Right };
        string? result = null;
        ok.Click += (_, _) => { result = tb.Text; win.DialogResult = true; };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Md3OnSurface"), Margin = new Thickness(0,0,0,12) });
        stack.Children.Add(tb);
        stack.Children.Add(ok);

        win.Content = new Border
        {
            CornerRadius = new CornerRadius(18), Padding = new Thickness(20),
            Background = (Brush)FindResource("Md3Surface"),
            BorderBrush = (Brush)FindResource("Md3Outline"), BorderThickness = new Thickness(1),
            Child = stack
        };
        tb.Loaded += (_, _) => { tb.SelectAll(); tb.Focus(); };
        return win.ShowDialog() == true ? result : null;
    }

    private void ManageGroups()
    {
        var groups = SettingsService.Current.Groups;
        if (groups.Count == 0)
        {
            CreateGroupOnly();
            return;
        }

        // Popup simple para renombrar/eliminar cada carpeta + crear nueva.
        var popup = new Popup
        {
            Placement = PlacementMode.Center, StaysOpen = false, AllowsTransparency = true,
            PlacementTarget = this
        };
        var panel = new StackPanel { Width = 260 };
        var box = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(10),
            Background = (Brush)FindResource("Md3SurfaceContainerHigh"),
            BorderBrush = (Brush)FindResource("Md3Outline"), BorderThickness = new Thickness(1),
            Child = panel
        };

        panel.Children.Add(new TextBlock { Text = Loc.T("Manage_FoldersTitle"), FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Md3OnSurface"), Margin = new Thickness(2,2,2,8) });

        foreach (var g in groups.ToList())
        {
            var row = new Grid { Margin = new Thickness(0,0,0,4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = g.Name, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Md3OnSurface") });

            var ren = new Button { Content = "✎", Style = (Style)FindResource("TitleBtn"), ToolTip = "Renombrar" };
            Grid.SetColumn(ren, 1);
            ren.Click += (_, _) =>
            {
                var nm = PromptText(Loc.T("Manage_RenameFolder"), g.Name);
                if (!string.IsNullOrWhiteSpace(nm)) { g.Name = nm.Trim(); SettingsService.Save(); }
                popup.IsOpen = false; BuildRows();
            };
            row.Children.Add(ren);

            var del = new Button { Content = "✕", Style = (Style)FindResource("TitleBtn"), ToolTip = "Eliminar" };
            Grid.SetColumn(del, 2);
            del.Click += (_, _) =>
            {
                foreach (var a in SettingsService.Current.Apps.Where(a => a.GroupId == g.Id)) a.GroupId = "";
                SettingsService.Current.Groups.Remove(g);
                SettingsService.Save();
                popup.IsOpen = false; BuildRows();
            };
            row.Children.Add(del);
            panel.Children.Add(row);
        }

        var newBtn = new Button { Content = "＋ " + Loc.T("Manage_NewFolder"), Style = (Style)FindResource("Md3TextButton"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,6,0,0) };
        newBtn.Click += (_, _) => { popup.IsOpen = false; CreateGroupOnly(); };
        panel.Children.Add(newBtn);

        popup.Child = box;
        popup.IsOpen = true;
    }

    private void CreateGroupOnly()
    {
        var name = PromptText(Loc.T("Manage_FolderNamePrompt"), Loc.T("Manage_FolderDefault"));
        if (string.IsNullOrWhiteSpace(name)) return;
        SettingsService.Current.Groups.Add(new AppGroup { Name = name.Trim() });
        SettingsService.Save();
        BuildRows();
    }

    private void PickIcon(AppEntry app)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Loc.T("Manage_ImageFilter")
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
        if (MessageBox.Show(string.Format(Loc.T("Common_RemoveApp"), app.Name), "QuickPanel",
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

    private Grid IconButton(string glyph, string tip, Action onClick)
        => IconButton(glyph, tip, _ => onClick());

    /// <summary>Botón de ícono.</summary>
    private Grid IconButton(string glyph, string tip, Action<UIElement> onClick)
    {
        var btn = new Button
        {
            Content = glyph,
            ToolTip = tip,
            Style   = (Style)FindResource("TitleBtn")
        };
        btn.Click += (s, _) => onClick((UIElement)s);

        var wrap = new Grid();
        wrap.Children.Add(btn);
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
