using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickPanel.Models;
using QuickPanel.Services;

namespace QuickPanel.Settings;

/// <summary>Editar nombre y URL de una app existente. No aplica cambios por sí mismo:
/// expone el resultado y <see cref="AppEditing"/> decide qué refrescar.</summary>
public partial class EditAppDialog : Window
{
    public string ResultName { get; private set; } = "";
    public string ResultUrl  { get; private set; } = "";

    public EditAppDialog(AppEntry app)
    {
        InitializeComponent();
        InName.Text = app.Name;
        InUrl.Text  = app.Url;

        var img = IconCache.Get(IconCache.KeyFor(app));
        if (img != null)
            IconHost.Child = new Image
            {
                Source = img, Width = 18, Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };

        Loaded += (_, _) => { InName.Focus(); InName.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = InUrl.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show(Loc.T("AddApp_NeedUrl"), "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            MessageBox.Show(Loc.T("AddApp_NeedUrl"), "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = InName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            try { name = new Uri(url).Host.Replace("www.", ""); }
            catch { name = url; }
        }

        ResultName   = name;
        ResultUrl    = url;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }
}

/// <summary>Flujo único de edición, compartido por Administrar apps y el clic derecho del
/// dock/menú: muestra el diálogo, aplica, persiste y refresca todas las vistas.</summary>
public static class AppEditing
{
    /// <summary>Devuelve true si la app cambió.</summary>
    public static bool Edit(AppEntry app, Window? owner)
    {
        var dlg = new EditAppDialog(app);
        if (owner != null) { try { dlg.Owner = owner; } catch { /* owner no apto: sin owner */ } }
        if (dlg.ShowDialog() != true) return false;

        bool nameChanged = dlg.ResultName != app.Name;
        bool urlChanged  = !string.Equals(dlg.ResultUrl, app.Url, StringComparison.OrdinalIgnoreCase);
        if (!nameChanged && !urlChanged) return false;

        app.Name = dlg.ResultName;

        if (urlChanged)
        {
            string OldHost() { try { return new Uri(app.Url).Host; } catch { return ""; } }
            string NewHost() { try { return new Uri(dlg.ResultUrl).Host; } catch { return ""; } }

            // El historial de otro sitio no sirve para la nueva URL.
            if (!string.Equals(OldHost(), NewHost(), StringComparison.OrdinalIgnoreCase))
                app.History.Clear();

            app.Url     = dlg.ResultUrl;
            app.Favicon = AppEntry.FaviconFor(dlg.ResultUrl);

            // El panel vivo sigue en la URL vieja: destruirlo para que se recree con la nueva.
            App.CloseAppPanels(app.Id);
            PanelPreviewCache.Remove(app.Id); // la captura es del sitio anterior
        }

        SettingsService.Save();
        App.RefreshAppLists();
        return true;
    }
}
