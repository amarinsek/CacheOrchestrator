namespace CacheOrchestrator.Admin;

/// <summary>Process-local Core override snapshot for one domain.</summary>
public sealed class DomainRuntimeOverride
{
    /// <summary>Monotonic mutation stamp.</summary>
    public int Stamp { get; init; }

    /// <summary>Runtime domain version, or null to inherit configuration.</summary>
    public string? Version { get; init; }

    /// <summary>Portable Data Cache enabled override.</summary>
    public bool? DataCacheEnabled { get; init; }

    /// <summary>Portable Data Cache TTL override.</summary>
    public TimeSpan? DataCacheTtl { get; init; }

    /// <summary>Whether at least one value is overridden.</summary>
    public bool HasAny => Version is not null || DataCacheEnabled is not null || DataCacheTtl is not null;
}

/// <summary>Partial Core settings update. Null values are unchanged.</summary>
public sealed class DomainSettingsPatch
{
    /// <summary>Portable Data Cache enabled.</summary>
    public bool? DataCacheEnabled { get; init; }

    /// <summary>Portable Data Cache TTL.</summary>
    public TimeSpan? DataCacheTtl { get; init; }

    /// <summary>Whether at least one value is supplied.</summary>
    public bool HasAny => DataCacheEnabled is not null || DataCacheTtl is not null;
}
