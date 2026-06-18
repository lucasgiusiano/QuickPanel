using System.Windows.Input;

namespace QuickPanel.Models;

/// <summary>Acciones globales que pueden dispararse con un hotkey (además de abrir apps).</summary>
public enum HotkeyAction
{
    ToggleMenu,
    HideActivePanel,
    NextApp,
    PrevApp,
    ToggleAutoHide,
    MoveButton
}

/// <summary>
/// Combinación de teclas serializable. Guarda el código virtual (VK) de la tecla
/// y los modificadores. Vacía (Key = None) significa "sin atajo".
/// </summary>
public class Hotkey
{
    public bool Ctrl  { get; set; }
    public bool Alt   { get; set; }
    public bool Shift { get; set; }
    public bool Win   { get; set; }

    /// <summary>Tecla principal como <see cref="System.Windows.Input.Key"/> (serializada por nombre).</summary>
    public Key Key { get; set; } = Key.None;

    public bool IsSet => Key != Key.None && (Ctrl || Alt || Shift || Win);

    /// <summary>Modificadores en formato Win32 (MOD_*) para RegisterHotKey.</summary>
    public uint Win32Modifiers
    {
        get
        {
            uint m = 0;
            if (Alt)   m |= 0x0001; // MOD_ALT
            if (Ctrl)  m |= 0x0002; // MOD_CONTROL
            if (Shift) m |= 0x0004; // MOD_SHIFT
            if (Win)   m |= 0x0008; // MOD_WIN
            return m;
        }
    }

    /// <summary>Virtual-key code para RegisterHotKey.</summary>
    public uint Win32Vk => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        if (!IsSet) return "Sin asignar";
        var parts = new List<string>();
        if (Ctrl)  parts.Add("Ctrl");
        if (Alt)   parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win)   parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public static Hotkey FromKeyEvent(Key key, ModifierKeys mods) => new()
    {
        Ctrl  = mods.HasFlag(ModifierKeys.Control),
        Alt   = mods.HasFlag(ModifierKeys.Alt),
        Shift = mods.HasFlag(ModifierKeys.Shift),
        Win   = mods.HasFlag(ModifierKeys.Windows),
        Key   = key
    };
}
