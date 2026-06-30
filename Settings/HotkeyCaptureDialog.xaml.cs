using System.Windows;
using System.Windows.Input;
using QuickPanel.Models;

using QuickPanel.Services;

namespace QuickPanel.Settings;

/// <summary>Diálogo modal que captura una combinación de teclas.</summary>
public partial class HotkeyCaptureDialog : Window
{
    public Hotkey? Result { get; private set; }

    public HotkeyCaptureDialog(string appName)
    {
        InitializeComponent();
        Prompt.Text = string.Format(Loc.T("Hotkey_Prompt"), appName);
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
            Prompt.Text = Loc.T("Hotkey_NeedModifier");
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
