namespace CacheOrchestrator.Admin;

/// <summary>
/// Canonical (v2) live stats: raw process counters only.
/// Shares, rates, pipeline, and impact KPIs are computed by consumers (Admin Console).
/// </summary>
public sealed class AdminLiveStatsRawSnapshot
{
    /// <summary>Instance identifier.</summary>
    public required string InstanceId { get; init; }

    /// <summary>UTC collection time.</summary>
    public DateTimeOffset CollectedAtUtc { get; init; }

    /// <summary>Per-domain raw counters.</summary>
    public required IReadOnlyList<AdminDomainCountersDto> Domains { get; init; }

    /// <summary>Endpoints without a known domain.</summary>
    public required IReadOnlyList<AdminEndpointCountersDto> UnassignedEndpoints { get; init; }

    /// <summary>Flat endpoint list (all domains + unassigned).</summary>
    public IReadOnlyList<AdminEndpointCountersDto> Endpoints { get; init; } = [];
}

/// <summary>Raw domain counters (Admin stats v2).</summary>
public sealed class AdminDomainCountersDto
{
    /// <summary>Normalized domain name.</summary>
    public required string Name { get; init; }

    /// <summary>Instance id when this row is instance-scoped; null for aggregates.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Effective Version when enriched by the query layer; otherwise empty.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>True when Version is a runtime override (enriched).</summary>
    public bool VersionIsRuntimeOverride { get; init; }

    /// <summary>Client Cache Schedule phase wire value when enriched.</summary>
    public string? SchedulePhase { get; init; }

    /// <summary>Last successful invalidation UTC.</summary>
    public DateTimeOffset? LastInvalidationUtc { get; init; }

    /// <summary>Successful invalidation count (process lifetime).</summary>
    public long Invalidations { get; init; }

    /// <summary>Output Cache hits.</summary>
    public long OcHits { get; init; }

    /// <summary>Output Cache misses.</summary>
    public long OcMisses { get; init; }

    /// <summary>Output Cache bypasses (auth / no-store).</summary>
    public long OcBypass { get; init; }

    /// <summary>Output Cache disabled for the domain.</summary>
    public long OcOff { get; init; }

    /// <summary>Fusion hits.</summary>
    public long FcHits { get; init; }

    /// <summary>Fusion misses (factory ran).</summary>
    public long FcMisses { get; init; }

    /// <summary>Fusion fail-safe stale serves.</summary>
    public long FcStale { get; init; }

    /// <summary>Fusion bypasses.</summary>
    public long FcBypass { get; init; }

    /// <summary>Times the value factory ran.</summary>
    public long FactoryRuns { get; init; }

    /// <summary>Times the value factory threw (fail-safe / error path).</summary>
    public long FactoryFailures { get; init; }

    /// <summary>
    /// Sum of factory-path durations in milliseconds (when latency tracking is on).
    /// Null when latency is not tracked.
    /// </summary>
    public double? FactoryDurationSumMs { get; init; }

    /// <summary>Number of factory-path duration samples in <see cref="FactoryDurationSumMs"/>.</summary>
    public long FactoryDurationCount { get; init; }

    /// <summary>
    /// Sum of measured factory result sizes in bytes (when result-size tracking is on and size is known).
    /// </summary>
    public long? FactoryResultSizeSumBytes { get; init; }

    /// <summary>Number of factory result size samples.</summary>
    public long FactoryResultSizeCount { get; init; }

    /// <summary>Endpoints attributed to this domain (when enriched).</summary>
    public IReadOnlyList<AdminEndpointCountersDto> Endpoints { get; init; } = [];
}

/// <summary>Raw endpoint counters (Admin stats v2).</summary>
public sealed class AdminEndpointCountersDto
{
    /// <summary>e.g. <c>GET /api/products/{id}</c>.</summary>
    public required string Route { get; init; }

    /// <summary>Instance id when row is instance-scoped.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Configured or runtime domain, if known.</summary>
    public string? ConfiguredDomain { get; init; }

    /// <summary>Output Cache hits.</summary>
    public long OcHits { get; init; }

    /// <summary>Output Cache misses.</summary>
    public long OcMisses { get; init; }

    /// <summary>Output Cache bypasses (auth / no-store).</summary>
    public long OcBypass { get; init; }

    /// <summary>Output Cache disabled for the domain.</summary>
    public long OcOff { get; init; }

    /// <summary>Fusion hits.</summary>
    public long FcHits { get; init; }

    /// <summary>Fusion misses.</summary>
    public long FcMisses { get; init; }

    /// <summary>Fusion stale serves.</summary>
    public long FcStale { get; init; }

    /// <summary>Fusion bypasses.</summary>
    public long FcBypass { get; init; }

    /// <summary>Times the value factory ran.</summary>
    public long FactoryRuns { get; init; }

    /// <summary>Times the value factory threw.</summary>
    public long FactoryFailures { get; init; }

    /// <summary>Sum of factory-path durations in milliseconds when latency tracking is on.</summary>
    public double? FactoryDurationSumMs { get; init; }

    /// <summary>Number of factory-path duration samples.</summary>
    public long FactoryDurationCount { get; init; }

    /// <summary>Sum of measured factory result sizes in bytes when tracking is on and size is known.</summary>
    public long? FactoryResultSizeSumBytes { get; init; }

    /// <summary>Number of factory result size samples.</summary>
    public long FactoryResultSizeCount { get; init; }
}
