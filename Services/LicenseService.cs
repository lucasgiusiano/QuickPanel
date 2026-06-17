namespace QuickPanel.Services;

/// <summary>
/// Planes del modelo freemium. El valor se persiste en settings (Tier) y, cuando
/// se implemente la verificación de Store IAP, <see cref="RefreshFromStoreAsync"/>
/// lo actualizará consultando los add-ons comprados.
/// </summary>
public enum LicenseTier
{
    Free,      // hasta 5 apps, con publicidad
    Clean,     // sin publicidad, 5 apps
    Pro,       // apps ilimitadas, con publicidad
    Complete   // sin publicidad + ilimitadas
}

public static class LicenseService
{
    /// <summary>Tope de apps para los planes con límite.</summary>
    public const int FreeAppLimit = 5;

    public static LicenseTier CurrentTier => SettingsService.Current.Tier;

    /// <summary>Máximo de apps permitido para el plan actual (int.MaxValue = ilimitado).</summary>
    public static int MaxApps => CurrentTier switch
    {
        LicenseTier.Pro or LicenseTier.Complete => int.MaxValue,
        _                                       => FreeAppLimit
    };

    /// <summary>True si todavía se puede agregar una app más con el plan actual.</summary>
    public static bool CanAddApp(int currentCount) => currentCount < MaxApps;

    /// <summary>True si el plan muestra publicidad.</summary>
    public static bool ShowsAds => CurrentTier is LicenseTier.Free or LicenseTier.Pro;

    /// <summary>
    /// Consulta la Store para determinar el plan comprado y lo guarda en settings.
    /// TODO: implementar con Windows.Services.Store.StoreContext una vez creados
    /// los add-ons en Partner Center (requiere empaquetado MSIX).
    /// </summary>
    public static Task RefreshFromStoreAsync()
    {
        // Placeholder: por ahora respeta el Tier persistido en settings.
        return Task.CompletedTask;
    }
}
