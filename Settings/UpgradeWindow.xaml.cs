using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using QuickPanel.Services;

namespace QuickPanel.Settings;

public partial class UpgradeWindow : Window
{
    private static readonly LicenseTier[] Order =
        { LicenseTier.Free, LicenseTier.Pro, LicenseTier.Complete };

    /// <param name="reason">Texto opcional que explica por qué se abrió (ej. feature bloqueada).</param>
    public UpgradeWindow(string? reason = null)
    {
        InitializeComponent();
        SubtitleText.Text = reason ?? "Desbloqueá más apps y funcionalidades.";
        BuildCards();
    }

    private void BuildCards()
    {
        Cards.Children.Clear();
        foreach (var tier in Order)
            Cards.Children.Add(BuildCard(tier));
    }

    private Border BuildCard(LicenseTier tier)
    {
        bool isCurrent   = tier == LicenseService.CurrentTier;
        bool isHighlight = tier == LicenseTier.Pro;

        var card = new Border
        {
            CornerRadius    = new CornerRadius(16),
            Margin          = new Thickness(8),
            Padding         = new Thickness(18),
            Background      = (Brush)FindResource(isHighlight ? "Md3SurfaceContainerHigh" : "Md3SurfaceContainer"),
            BorderThickness = new Thickness(isHighlight ? 2 : 1),
            BorderBrush     = (Brush)FindResource(isHighlight ? "Md3Primary" : "Md3Outline")
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Header: nombre + precio ──
        var head = new StackPanel();
        head.Children.Add(new TextBlock
        {
            Text       = LicenseService.Name(tier),
            FontSize   = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource(isHighlight ? "Md3Primary" : "Md3OnSurface")
        });
        head.Children.Add(new TextBlock
        {
            Text       = LicenseService.Price(tier),
            FontSize   = 13,
            Margin     = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)FindResource("Md3OnSurfaceVariant")
        });
        Grid.SetRow(head, 0);
        grid.Children.Add(head);

        // ── Bullets ──
        var bullets = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var line in LicenseService.Highlights(tier))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            row.Children.Add(new TextBlock
            {
                Text       = "•",
                FontSize   = 12,
                Margin     = new Thickness(0, 0, 8, 0),
                Foreground = (Brush)FindResource("Md3Primary")
            });
            row.Children.Add(new TextBlock
            {
                Text         = line,
                FontSize     = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = (Brush)FindResource("Md3OnSurface")
            });
            bullets.Children.Add(row);
        }
        Grid.SetRow(bullets, 1);
        grid.Children.Add(bullets);

        // ── Botón ──
        Button btn;
        if (isCurrent)
        {
            btn = new Button
            {
                Content   = "Plan actual",
                IsEnabled = false,
                Style     = (Style)FindResource("Md3TextButton")
            };
        }
        else if (tier == LicenseTier.Free)
        {
            // No se "compra" Free; no mostramos botón de acción.
            btn = new Button { Visibility = Visibility.Hidden, Style = (Style)FindResource("Md3TextButton") };
        }
        else
        {
            btn = new Button
            {
                Content = "Obtener",
                Style   = (Style)FindResource("Md3FilledButton"),
                Tag     = tier
            };
            btn.Click += Buy_Click;
        }
        btn.Margin             = new Thickness(0, 14, 0, 0);
        btn.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(btn, 2);
        grid.Children.Add(btn);

        card.Child = grid;
        return card;
    }

    private async void Buy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LicenseTier tier }) return;

        // TODO (IAP): reemplazar por la compra real vía StoreContext.RequestPurchaseAsync(addOnStoreId)
        // y, ante éxito, LicenseService.RefreshFromStoreAsync() para releer el plan.
#if DEBUG
        // En desarrollo: aplicar el plan localmente para poder testear el gating.
        SettingsService.Current.Tier = tier;
        SettingsService.Save();
        await LicenseService.RefreshFromStoreAsync();
        BuildCards();
#else
        await Task.CompletedTask;
        MessageBox.Show(
            "Las compras estarán disponibles próximamente.",
            "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
