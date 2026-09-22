using System.Windows;
using System.Windows.Interop;
using QuickPanel.Services;

namespace QuickPanel.Settings;

public partial class ReviewPromptWindow : Window
{
    public ReviewPromptWindow()
    {
        InitializeComponent();
    }

    private void Rate_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ReviewPromptService.OpenReview(hwnd);
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();
}
