using System.Windows;
using System.Windows.Input;
using QuickPanel.Models;

namespace QuickPanel.Settings;

/// <summary>Diálogo modal que captura una combinación de teclas.</summary>
public partial class HotkeyCaptureDialog : Window
{
    public Hotkey? Result { get; private set; }

    public HotkeyCaptureDialog(string appName)
    {
        InitializeComponent();
        Prompt.Text = $"Presioná la combinación para «{appName}»";
        Focusable = true;
        Loaded += (_, _) => Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape) { DialogResult = false; return; }

        // Ignorar pulsaciones de solo-modificador: esperamos una tecla real.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                 or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            Prompt.Text = "Usá al menos un modificador (Ctrl, Alt, Shift o Win).";
            return;
        }

        Result = Hotkey.FromKeyEvent(key, mods);
        DialogResult = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Result = new Hotkey(); // sin asignar
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
