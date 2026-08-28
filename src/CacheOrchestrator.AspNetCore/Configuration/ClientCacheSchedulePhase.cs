namespace CacheOrchestrator.Configuration;

/// <summary>Phase used by Client Cache Schedule when generating Cache-Control.</summary>
public enum ClientCacheSchedulePhase
{
    /// <summary>Far from the scheduled update; use maximum Client Cache TTL.</summary>
    Calm = 0,

    /// <summary>Within the ramp window before the scheduled update.</summary>
    Approaching = 1,

    /// <summary>Past the scheduled update; hold max-age at the configured floor.</summary>
    Hold = 2,

    /// <summary>No schedule or Client Cache is disabled.</summary>
    NotApplicable = 3
}
