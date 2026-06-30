using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickPanel.Models;

using QuickPanel.Services;

namespace QuickPanel.Settings;

public partial class AddAppDialog : Window
{
    public AppEntry? Result { get; private set; }

    private static readonly (string Name, string Url)[] Presets =
    {
        ("WhatsApp", "https://web.whatsapp.com"),
        ("Gmail",    "https://mail.google.com"),
        ("Claude",   "https://claude.ai"),
        ("ChatGPT",  "https://chat.openai.com"),
        ("Notion",   "https://www.notion.so"),
        ("Spotify",  "https://open.spotify.com"),
        ("YouTube",  "https://www.youtube.com"),
        ("Calendar", "https://calendar.google.com"),
        ("Telegram", "https://web.telegram.org"),
        ("Discord",  "https://discord.com/app"),
        ("Drive",    "https://drive.google.com"),
        ("X",        "https://x.com"),
    };

    public AddAppDialog()
    {
        InitializeComponent();
        BuildPresets();
    }

    private void BuildPresets()
    {
        foreach (var (name, url) in Presets)
        {
            var border = new Border
            {
                Margin       = new Thickness(4),
                Padding      = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(12),
                Background   = (Brush)FindResource("Md3SurfaceContainer"),
                Cursor       = Cursors.Hand
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource   = new Uri(AppEntry.FaviconFor(url));
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                sp.Children.Add(new System.Windows.Controls.Image
                {
                    Source            = img,
                    Width             = 18,
                    Height            = 18,
                    Margin            = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            catch { }

            sp.Children.Add(new TextBlock
            {
                Text              = name,
                FontSize          = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = (Brush)FindResource("Md3OnSurface")
            });

            border.Child = sp;
            border.MouseLeftButtonUp += (_, _) =>
            {
                Result       = new AppEntry { Name = name, Url = url, Favicon = AppEntry.FaviconFor(url) };
                DialogResult = true;
            };
            PresetList.Items.Add(border);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = InNombre.Text.Trim();
        var url  = InUrl.Text.Trim();

        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show(Loc.T("AddApp_NeedUrl"), "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
        if (string.IsNullOrEmpty(name))
        {
            try { name = new Uri(url).Host.Replace("www.", ""); }
            catch { name = url; }
        }

        Result       = new AppEntry { Name = name, Url = url, Favicon = AppEntry.FaviconFor(url) };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
