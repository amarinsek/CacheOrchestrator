using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Physical get-or-create inputs for an <see cref="IDataCacheProvider"/>.
/// </summary>
/// <remarks>
/// Built by <see cref="ICacheOrchestrator"/> after domain policy resolution.
/// <see cref="DomainOptions"/> remains on the request until domain options are split per package;
/// providers that need engine-specific entry options read from it.
/// </remarks>
public sealed class DataCacheProviderRequest
{
    /// <summary>Fully formed cache key (includes domain + Version hex + logical key).</summary>
    public required string Key { get; init; }

    /// <summary>Named data-cache instance (today: FusionCache instance name).</summary>
    public required string InstanceName { get; init; }

    /// <summary>Tags to attach on set (domain / entity / entitykind / extras).</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>Resolved domain snapshot for TTL and provider-specific knobs.</summary>
    public required DomainCacheOptions DomainOptions { get; init; }
}
