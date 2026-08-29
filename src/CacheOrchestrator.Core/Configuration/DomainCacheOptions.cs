namespace CacheOrchestrator.Configuration;

/// <summary>
/// Resolved HTTP-free domain snapshot consumed by Core orchestration and Data Cache providers.
/// </summary>
public sealed class DomainCacheOptions
{
#pragma warning disable IDE0032 // Manual fields support thread-safe lazy hot-path tag preparation.
    private string? _domainTag;
    private string[]? _domainTags;
    private string? _physicalKeyPrefix;
#pragma warning restore IDE0032

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

    /// <summary>Prepared immutable domain tag reused by Data Cache requests for this snapshot.</summary>
    internal string DomainTag
    {
        get
        {
            string? cached = Volatile.Read(ref _domainTag);
            if (cached is not null)
                return cached;

            string created = CacheTags.Domain(Domain);
            return Interlocked.CompareExchange(ref _domainTag, created, null) ?? created;
        }
    }

    /// <summary>Prepared domain-only tag collection reused by Data Cache requests for this snapshot.</summary>
    internal string[] DomainTags
    {
        get
        {
            string[]? cached = Volatile.Read(ref _domainTags);
            if (cached is not null)
                return cached;

            string[] created = [DomainTag];
            return Interlocked.CompareExchange(ref _domainTags, created, null) ?? created;
        }
    }

    /// <summary>Prepared unambiguous provider-key prefix reused by this snapshot.</summary>
    internal string PhysicalKeyPrefix
    {
        get
        {
            string? cached = Volatile.Read(ref _physicalKeyPrefix);
            if (cached is not null)
                return cached;

            string created = string.Concat(
                "co3:",
                Uri.EscapeDataString(Domain),
                ":",
                VersionHex,
                ":");
            return Interlocked.CompareExchange(ref _physicalKeyPrefix, created, null) ?? created;
        }
    }
}
