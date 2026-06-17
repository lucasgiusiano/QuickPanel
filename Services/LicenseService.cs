namespace QuickPanel.Services;

/// <summary>Planes del modelo freemium.</summary>
public enum LicenseTier
{
    Free,
    Pro,
    Complete
}

/// <summary>
/// Funcionalidades que pueden estar detrás de un plan. Cada feature se mapea a un
/// <see cref="LicenseTier"/> mínimo en <see cref="LicenseService.MinTierFor"/>.
/// </summary>
public enum Feature
{
    UnlimitedApps,
    FreePanelSize,
    ReorderApps,
    GlobalHotkeys,
    Notifications,
    AutoHide,
    CompactMode,
    QuickSearch,
    CustomIcons,
    CustomNames,
    LightTheme,
    PremiumPalettes,
    StartApp,
    History,
    // Complete:
    PictureInPicture,
    MultiAccount,
    Profiles,
    ImportExport,
    Folders,
    PerAppColor,
    MenuButtonSize
}

public static class LicenseService
{
    /// <summary>Tope de apps del plan gratuito.</summary>
    public const int FreeAppLimit = 3;

    public static LicenseTier CurrentTier => SettingsService.Current.Tier;

    /// <summary>Plan mínimo que desbloquea cada feature.</summary>
    public static LicenseTier MinTierFor(Feature f) => f switch
    {
        Feature.PictureInPicture or Feature.MultiAccount or Feature.Profiles or
        Feature.ImportExport or Feature.Folders or Feature.PerAppColor or
        Feature.MenuButtonSize => LicenseTier.Complete,

        _ => LicenseTier.Pro
    };

    /// <summary>True si el plan actual desbloquea la feature.</summary>
    public static bool HasFeature(Feature f) => CurrentTier >= MinTierFor(f);

    // ── Límite de apps ──

    public static int MaxApps => CurrentTier switch
    {
        LicenseTier.Pro or LicenseTier.Complete => int.MaxValue,
        _                                       => FreeAppLimit
    };

    public static bool CanAddApp(int currentCount) => currentCount < MaxApps;

    // ── Metadata para la UI de planes ──

    public static string Name(LicenseTier t) => t switch
    {
        LicenseTier.Free     => "Free",
        LicenseTier.Pro      => "Pro",
        LicenseTier.Complete => "Complete",
        _                    => t.ToString()
    };

    public static string Price(LicenseTier t) => t switch
    {
        LicenseTier.Free     => "Gratis",
        LicenseTier.Pro      => "USD 4.99",
        LicenseTier.Complete => "USD 9.99",
        _                    => ""
    };

    /// <summary>Bullets que se muestran en la tarjeta de cada plan.</summary>
    public static string[] Highlights(LicenseTier t) => t switch
    {
        LicenseTier.Free => new[]
        {
            "Hasta 3 apps",
            "Tema oscuro",
            "Panel anclado a Edge"
        },
        LicenseTier.Pro => new[]
        {
            "Apps ilimitadas",
            "Atajos de teclado globales",
            "Notificaciones del sistema",
            "Auto-ocultar y modo compacto",
            "Búsqueda rápida",
            "Íconos y nombres personalizados",
            "Temas claro/sistema + paletas premium",
            "Tamaño de panel libre · Historial"
        },
        LicenseTier.Complete => new[]
        {
            "Todo lo de Pro, y además:",
            "Picture-in-picture",
            "Múltiples cuentas por app",
            "Múltiples perfiles",
            "Grupos / carpetas",
            "Color por app",
            "Exportar / importar config"
        },
        _ => System.Array.Empty<string>()
    };

    /// <summary>
    /// Determina el plan comprado consultando la Store y lo persiste.
    /// TODO: implementar con Windows.Services.Store.StoreContext una vez creados
    /// los add-ons en Partner Center (requiere empaquetado MSIX).
    /// </summary>
    public static Task RefreshFromStoreAsync() => Task.CompletedTask;
}
