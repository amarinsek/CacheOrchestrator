namespace CacheOrchestrator.Configuration;

/// <summary>Outcome of HTTP Data Cache handling for a request.</summary>
public enum DataCacheResult : byte
{
    /// <summary>Value served without factory execution.</summary>
    Hit = 0,

    /// <summary>Value produced by the factory and stored.</summary>
    Miss = 1,

    /// <summary>Stale value served after factory failure.</summary>
    Stale = 2,

    /// <summary>Caching skipped for this request.</summary>
    Bypass = 3,

    /// <summary>Data Cache disabled for the domain.</summary>
    Off = 4,

    /// <summary>No request domain could be resolved.</summary>
    Unresolved = 5
}
