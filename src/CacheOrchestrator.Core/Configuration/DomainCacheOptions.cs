namespace CacheOrchestrator.Configuration;

/// <summary>
/// Resolved HTTP-free domain snapshot consumed by Core orchestration and Data Cache providers.
/// </summary>
public sealed class DomainCacheOptions
{
    /// <summary>Normalized domain name.</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>
    /// Name of the Data Cache instance that handles this domain.
    /// Matches a key in <see cref="CacheOrchestratorOptions.DataCacheInstances"/>.
    /// </summary>
    public string DataCacheInstanceName { get; init; } = "default";

    /// <summary>Whether Data Cache is enabled for this domain.</summary>
    public bool DataCacheEnabled { get; init; } = true;

    /// <summary>Version token used for cache generations and coordinated invalidation.</summary>
    public string Version { get; init; } = "1";

    /// <summary>Hex representation of the XxHash3 of <see cref="Version"/> used in cache keys.</summary>
    public string VersionHex { get; init; } = string.Empty;

    /// <summary>Logical Data Cache TTL used by the selected provider.</summary>
    public TimeSpan DataCacheTtl { get; init; }

    /// <summary>Key prefix / namespace for the selected Data Cache instance.</summary>
    public string DataCacheNamespace { get; init; } = string.Empty;
}
