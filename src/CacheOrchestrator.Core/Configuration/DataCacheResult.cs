namespace CacheOrchestrator.Configuration;

/// <summary>
/// Outcome of the data-cache layer for a request.
/// </summary>
public enum DataCacheResult : byte
{
    /// <summary>Value served from cache without factory execution.</summary>
    Hit = 0,

    /// <summary>Value produced by the factory and stored.</summary>
    Miss = 1,

    /// <summary>Stale value served after factory failure (fail-safe).</summary>
    Stale = 2,

    /// <summary>Caching skipped for this request (e.g. no-store).</summary>
    Bypass = 3,

    /// <summary>Data cache disabled for the domain.</summary>
    Off = 4,

    /// <summary>
    /// No domain options on the request and none resolved from endpoint metadata;
    /// factory ran without caching.
    /// </summary>
    Unresolved = 5
}