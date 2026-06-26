using System.Windows.Input;
using System.Windows.Interop;
using QuickPanel.Core;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>Lo que el HotkeyService necesita del mundo: cuál es el overlay activo.</summary>
public interface IHotkeyTarget
{
    /// <summary>Overlay de la ventana de Edge en foco; fallback a la última activa o la primera.</summary>
    OverlayManager? ActiveOverlay { get; }
}

/// <summary>
/// Registra atajos globales con RegisterHotKey usando una ventana-mensajero invisible
/// (HwndSource). Cada hotkey registrado lleva un id; al recibir WM_HOTKEY se ejecuta
/// la acción asociada sobre el overlay activo.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly IHotkeyTarget _target;
    private readonly Dictionary<int, Action<OverlayManager>> _actions = new();
    private int _nextId = 1;

    public HotkeyService(IHotkeyTarget target)
    {
        _target = target;

        // Ventana-mensajero invisible (HWND_MESSAGE) que vive mientras corre la app.
        var prm = new HwndSourceParameters("QuickPanelHotkeys")
        {
            Width = 0, Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _source = new HwndSource(prm);
        _source.AddHook(WndProc);

        Reload();
    }

    /// <summary>Re-registra todos los hotkeys desde la configuración actual.</summary>
    public void Reload()
    {
        UnregisterAll();

        var s = SettingsService.Current;

        foreach (var app in s.Apps)
        {
            if (!app.Hotkey.IsSet) continue;
            string id = app.Id;
            Register(app.Hotkey, mgr => mgr.OpenAppById(id));
        }

        foreach (var (name, hk) in s.ActionHotkeys)
        {
            if (hk == null || !hk.IsSet) continue;
            if (!Enum.TryParse<HotkeyAction>(name, out var action)) continue;
            Register(hk, mgr => RunAction(mgr, action));
        }
    }

    private static void RunAction(OverlayManager mgr, HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleMenu:      mgr.ToggleMenu();       break;
            case HotkeyAction.HideActivePanel: mgr.HideActivePanel();  break;
            case HotkeyAction.NextApp:         mgr.CycleApp(+1);       break;
            case HotkeyAction.PrevApp:         mgr.CycleApp(-1);       break;
            case HotkeyAction.ToggleAutoHide:  mgr.ToggleAutoHide();   break;
            case HotkeyAction.MoveButton:      mgr.EnterMoveMode();    break;
        }
    }

    /// <summary>Registra un hotkey. Devuelve false si la combinación ya está tomada.</summary>
    private bool Register(Hotkey hk, Action<OverlayManager> onPressed)
    {
        int id = _nextId++;
        bool ok = Win32.RegisterHotKey(_source.Handle, id,
            hk.Win32Modifiers | Win32.MOD_NOREPEAT, hk.Win32Vk);
        if (ok) _actions[id] = onPressed;
        return ok;
    }

    /// <summary>Prueba si una combinación está libre (registra y desregistra al instante).</summary>
    public bool IsAvailable(Hotkey hk)
    {
        int id = 9000 + _nextId;
        bool ok = Win32.RegisterHotKey(_source.Handle, id, hk.Win32Modifiers | Win32.MOD_NOREPEAT, hk.Win32Vk);
        if (ok) Win32.UnregisterHotKey(_source.Handle, id);
        return ok;
    }

    private void UnregisterAll()
    {
        foreach (var id in _actions.Keys) Win32.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var act))
        {
            var mgr = _target.ActiveOverlay;
            if (mgr != null) act(mgr);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
