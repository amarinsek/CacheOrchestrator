using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Physical get-or-create inputs for an <see cref="IDataCacheProvider"/>.
/// </summary>
/// <remarks>
/// Built by <see cref="ICacheOrchestrator"/> after HTTP-free domain policy resolution.
/// Engine-specific settings are resolved by the provider package and are not carried by this contract.
/// </remarks>
public sealed class DataCacheProviderRequest
{
    /// <summary>
    /// Fully formed orchestrator key (includes domain + Version hex + logical key).
    /// A provider still owns any engine-level namespace prefix.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Named data-cache instance (from <c>Cache:DataCacheInstances</c>).</summary>
    public required string InstanceName { get; init; }

    /// <summary>Tags to attach on set (domain / entity / entitykind / extras).</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>Resolved HTTP-free domain snapshot for Data Cache policy.</summary>
    public required DomainCacheOptions DomainOptions { get; init; }
}
