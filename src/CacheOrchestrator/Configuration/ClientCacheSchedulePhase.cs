namespace CacheOrchestrator.Configuration;

/// <summary>
/// Phase of the Client Cache Schedule used when building the client <c>Cache-Control</c> header.
/// </summary>
public enum ClientCacheSchedulePhase
{
    /// <summary>Far from <see cref="DomainCacheOptions.ScheduledUpdateUtc"/> – using max client TTL.</summary>
    Calm = 0,

    /// <summary>Within the ramp window before the scheduled cutover.</summary>
    Approaching = 1,

    /// <summary>
    /// Now is past the <see cref="DomainCacheOptions.ScheduledUpdateUtc"/>.
    /// Max-age is held at the floor until the schedule is cleared or moved forward.
    /// </summary>
    Hold = 2,

    // TODO: v3.0.0 Change to NotApplicable = 3

    /// <summary>NoStore, no schedule, or client caching blocked – schedule not applied.</summary>
    NotApplicable = 4
}
