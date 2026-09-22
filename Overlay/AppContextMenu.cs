using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using QuickPanel.Core;
using QuickPanel.Models;
using QuickPanel.Services;

namespace QuickPanel.Overlay;

/// <summary>
/// Menú de clic derecho de un ícono de app (Dock y menú Material): Editar / Quitar.
/// Popup con la paleta MD3 en vez del ContextMenu nativo (que siempre abre en blanco).
/// Las acciones se difieren al dispatcher: abren diálogos modales, y hacerlo dentro del
/// handler del Popup lo deja a medio cerrar.
/// </summary>
internal static class AppContextMenu
{
    /// <summary>True mientras hay un menú abierto: el dock no colapsa (dejaría el popup
    /// anclado a un ícono oculto).</summary>
    public static bool IsOpen { get; private set; }

    public static void Show(UIElement anchor, AppEntry app, OverlayManager manager)
    {
        var popup = new Popup
        {
            PlacementTarget    = anchor,
            Placement          = PlacementMode.Left,
            HorizontalOffset   = -6,
            StaysOpen          = false,
            AllowsTransparency = true,
            PopupAnimation     = PopupAnimation.Fade
        };

        var res = Application.Current;
        var panel = new StackPanel { MinWidth = 150 };
        var box = new Border
        {
            CornerRadius    = new CornerRadius(12),
            Padding         = new Thickness(4),
            Margin          = new Thickness(8),
            Background      = (Brush)res.FindResource("Md3SurfaceContainerHigh"),
            BorderBrush     = (Brush)res.FindResource("Md3Outline"),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black },
            Child = panel
        };

        panel.Children.Add(new TextBlock
        {
            Text = app.Name, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 6, 10, 4), TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200,
            Foreground = (Brush)res.FindResource("Md3OnSurfaceVariant")
        });

        void Row(string glyph, string text, Action act)
        {
            var b = new Button
            {
                Content = $"{glyph}   {text}",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = (Style)res.FindResource("Md3TextButton")
            };
            b.Click += (_, _) =>
            {
                popup.IsOpen = false;
                anchor.Dispatcher.BeginInvoke(act,
                    System.Windows.Threading.DispatcherPriority.Background);
            };
            panel.Children.Add(b);
        }

        Row("✎", Loc.T("Common_Edit"), () => manager.EditApp(app));
        Row("✕", Loc.T("Common_Remove"), () =>
        {
            if (MessageBox.Show(string.Format(Loc.T("Common_RemoveApp"), app.Name), "QuickPanel",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                manager.RemoveApp(app);
        });

        popup.Child = box;
        popup.Closed += (_, _) => IsOpen = false;
        IsOpen = true;
        popup.IsOpen = true;
    }
}
