namespace QuickPanel.Services.CloudSync;

/// <summary>Cada cuánto se revisa si hay cambios para subir/bajar automáticamente.</summary>
public enum SyncInterval
{
    /// <summary>Nunca automático; solo con los botones Subir/Bajar.</summary>
    ManualOnly,

    /// <summary>Al cerrar la app (default). Sube lo pendiente antes de salir.</summary>
    OnAppClose,

    /// <summary>Cada 15 minutos.</summary>
    Every15Min,

    /// <summary>Cada hora.</summary>
    Hourly,

    /// <summary>Al instante tras cada cambio, con debounce (agrupa cambios seguidos).</summary>
    Realtime
}

public static class SyncIntervalExtensions
{
    /// <summary>
    /// Periodo del timer para el chequeo de "hay algo dirty / cambió la nube".
    /// null = sin timer (ManualOnly, OnAppClose, Realtime que usa debounce propio).
    /// </summary>
    public static TimeSpan? TimerPeriod(this SyncInterval i) => i switch
    {
        SyncInterval.Every15Min => TimeSpan.FromMinutes(15),
        SyncInterval.Hourly     => TimeSpan.FromHours(1),
        _                       => null
    };
}
