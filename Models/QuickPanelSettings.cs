using QuickPanel.Services;

namespace QuickPanel.Models;

/// <summary>Estilo del menú de apps.</summary>
public enum MenuMode
{
    /// <summary>Botón flotante redondo + menú radial (Material Design). Default.</summary>
    Material,
    /// <summary>Barra lateral auto-ocultable anclada al borde derecho del navegador.</summary>
    Dock
}

public class QuickPanelSettings
{
    /// <summary>Color semilla del esquema MD3 (hex).</summary>
    public string SeedColor { get; set; } = "#6366F1";

    /// <summary>Modo de tema: oscuro (default), claro o según el sistema.</summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;

    /// <summary>Tamaño de las ventanas de app (DIPs).</summary>
    public double PanelWidth { get; set; } = 440;
    public double PanelHeight { get; set; } = 760;

    /// <summary>Diámetro de los círculos del menú flotante en DIPs.</summary>
    public double MenuItemSize { get; set; } = 48;

    /// <summary>Estilo del menú: botón flotante Material (default) o barra Dock lateral.</summary>
    public MenuMode MenuMode { get; set; } = MenuMode.Material;

    /// <summary>Idioma de la interfaz. null = automático (seguir el idioma de Windows).</summary>
    public Lang? Language { get; set; } = null;

    /// <summary>Posición del botón relativa al rect de la ventana de Edge (0..1).</summary>
    public double ButtonRelX { get; set; } = 0.97;
    public double ButtonRelY { get; set; } = 0.92;

    public bool RunAtStartup { get; set; } = true;

    /// <summary>Id de la app a abrir automáticamente al iniciar. Vacío = ninguna.</summary>
    public string StartAppId { get; set; } = "";

    /// <summary>Auto-ocultar el botón flotante cuando el mouse está lejos.</summary>
    public bool AutoHide { get; set; } = false;

    /// <summary>Mostrar contadores de no leídos. Desactivable.</summary>
    public bool ShowBadges { get; set; } = true;

    /// <summary>Modo Lite: optimiza RAM (suspende paneles ocultos, baja memoria,
    /// tope de paneles vivos, apaga autofill). Para equipos con poca memoria.</summary>
    public bool LiteMode { get; set; } = false;

    /// <summary>Atajos globales de acciones. Clave = nombre de HotkeyAction.</summary>
    public Dictionary<string, Hotkey> ActionHotkeys { get; set; } = new();

    public List<AppEntry> Apps { get; set; } = new();

    /// <summary>Carpetas para agrupar apps en el menú.</summary>
    public List<AppGroup> Groups { get; set; } = new();
}
