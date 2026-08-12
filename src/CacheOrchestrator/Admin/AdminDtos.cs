namespace CacheOrchestrator.Admin;

/// <summary>OC layer counters for Local Admin API responses.</summary>
public sealed class AdminLayerDto
{
    /// <summary>Cache hits.</summary>
    public long Hits { get; init; }

    /// <summary>Cache misses.</summary>
    public long Misses { get; init; }

    /// <summary>Bypassed requests (not counted in hit rate).</summary>
    public long Bypass { get; init; }

    /// <summary><c>hits / (hits + misses)</c>, or null when no traffic.</summary>
    public double? HitRate { get; init; }
}

/// <summary>Fusion layer counters for Local Admin API responses.</summary>
public sealed class AdminFusionLayerDto
{
    /// <summary>Cache hits.</summary>
    public long Hits { get; init; }

    /// <summary>Cache misses.</summary>
    public long Misses { get; init; }

    /// <summary>Bypassed requests (not counted in hit rate).</summary>
    public long Bypass { get; init; }

    /// <summary>Fail-safe stale serves.</summary>
    public long Stale { get; init; }

    /// <summary>Times the value factory ran.</summary>
    public long FactoryRuns { get; init; }

    /// <summary>Times the value factory threw (before fail-safe, if any).</summary>
    public long FactoryFailures { get; init; }

    /// <summary><c>hits / (hits + misses)</c>, or null when no traffic.</summary>
    public double? HitRate { get; init; }
}

/// <summary>Full live stats payload from one instance.</summary>
public sealed class AdminLiveStatsSnapshot
{
    /// <summary>Instance identifier.</summary>
    public required string InstanceId { get; init; }

    /// <summary>UTC collection time.</summary>
    public DateTimeOffset CollectedAtUtc { get; init; }

    /// <summary>Per-domain aggregates (may include nested endpoints).</summary>
    public required IReadOnlyList<AdminDomainStatsDto> Domains { get; init; }

    /// <summary>Endpoints without a known domain (or only runtime-unassigned traffic).</summary>
    public required IReadOnlyList<AdminEndpointStatsDto> UnassignedEndpoints { get; init; }
}

/// <summary>Domain-level live stats.</summary>
public sealed class AdminDomainStatsDto
{
    /// <summary>Normalized domain name.</summary>
    public required string Name { get; init; }

    /// <summary>Effective Version (config + runtime overlay).</summary>
    public required string Version { get; init; }

    /// <summary>True when Version comes from a runtime override.</summary>
    public bool VersionIsRuntimeOverride { get; init; }

    /// <summary>Client Cache Schedule phase at collection time, or null if n/a.</summary>
    public string? SchedulePhase { get; init; }

    /// <summary>Last successful invalidation UTC, if any.</summary>
    public DateTimeOffset? LastInvalidationUtc { get; init; }

    /// <summary>Successful invalidation count (lifetime of process).</summary>
    public long Invalidations { get; init; }

    /// <summary>Output Cache counters.</summary>
    public required AdminLayerDto Oc { get; init; }

    /// <summary>FusionCache counters.</summary>
    public required AdminFusionLayerDto Fc { get; init; }

    /// <summary>Endpoints attributed to this domain.</summary>
    public IReadOnlyList<AdminEndpointStatsDto> Endpoints { get; init; } = [];
}

/// <summary>Endpoint-level live stats.</summary>
public sealed class AdminEndpointStatsDto
{
    /// <summary>e.g. <c>GET /api/products/{id}</c>.</summary>
    public required string Route { get; init; }

    /// <summary>Configured domain from metadata, if fixed.</summary>
    public string? ConfiguredDomain { get; init; }

    /// <summary>Output Cache counters.</summary>
    public required AdminLayerDto Oc { get; init; }

    /// <summary>FusionCache counters.</summary>
    public required AdminFusionLayerDto Fc { get; init; }
}

/// <summary>Discovered endpoint metadata.</summary>
public sealed class AdminEndpointInfoDto
{
    /// <summary>e.g. <c>GET /api/products/{id}</c>.</summary>
    public required string Route { get; init; }

    /// <summary>HTTP method.</summary>
    public required string Method { get; init; }

    /// <summary>Route pattern raw text.</summary>
    public required string Pattern { get; init; }

    /// <summary>Fixed domain from metadata, if any.</summary>
    public string? ConfiguredDomain { get; init; }

    /// <summary>Optional display name (controller/action).</summary>
    public string? DisplayName { get; init; }
}

/// <summary>Effective domain configuration snapshot for Admin.</summary>
public sealed class AdminDomainConfigDto
{
    /// <summary>Normalized domain name.</summary>
    public required string Name { get; init; }

    /// <summary>Effective Version.</summary>
    public required string Version { get; init; }

    /// <summary>True when Version is a runtime override.</summary>
    public bool VersionIsRuntimeOverride { get; init; }

    /// <summary>Output Cache enabled.</summary>
    public bool OutputCacheEnabled { get; init; }

    /// <summary>FusionCache enabled.</summary>
    public bool FusionCacheEnabled { get; init; }

    /// <summary>Fusion instance name.</summary>
    public required string FusionCacheInstanceName { get; init; }

    /// <summary>Output Cache TTL seconds.</summary>
    public int OutputCacheTtlSeconds { get; init; }

    /// <summary>Fusion soft TTL seconds.</summary>
    public int FusionCacheSoftTtlSeconds { get; init; }

    /// <summary>Fusion hard TTL seconds.</summary>
    public int FusionCacheHardTtlSeconds { get; init; }

    /// <summary>Fusion fail-safe seconds.</summary>
    public int FusionCacheFailSafeSeconds { get; init; }

    /// <summary>Client TTL seconds.</summary>
    public int ClientTtlSeconds { get; init; }

    /// <summary>Client TTL min seconds.</summary>
    public int ClientTtlMinSeconds { get; init; }

    /// <summary>Scheduled update UTC, if any.</summary>
    public DateTimeOffset? ScheduledUpdateUtc { get; init; }

    /// <summary>Current schedule phase wire value.</summary>
    public string? SchedulePhase { get; init; }

    /// <summary>Which fields are runtime overrides.</summary>
    public AdminRuntimeOverrideFlagsDto? RuntimeOverrides { get; init; }
}

/// <summary>Flags indicating which effective values come from runtime overlay.</summary>
public sealed class AdminRuntimeOverrideFlagsDto
{
    /// <summary>Version overridden.</summary>
    public bool Version { get; init; }

    /// <summary>Output TTL overridden.</summary>
    public bool OutputCacheTtl { get; init; }

    /// <summary>Fusion soft TTL overridden.</summary>
    public bool FusionCacheSoftTtl { get; init; }

    /// <summary>Fusion hard TTL overridden.</summary>
    public bool FusionCacheHardTtl { get; init; }

    /// <summary>Fusion fail-safe overridden.</summary>
    public bool FusionCacheFailSafe { get; init; }

    /// <summary>Client TTL overridden.</summary>
    public bool ClientTtl { get; init; }

    /// <summary>Client min TTL overridden.</summary>
    public bool ClientTtlMin { get; init; }
}

/// <summary>Local Admin health response.</summary>
public sealed class AdminHealthDto
{
    /// <summary>Always true when the endpoint responds.</summary>
    public bool Healthy { get; init; } = true;

    /// <summary>Instance id.</summary>
    public required string InstanceId { get; init; }

    /// <summary>UTC now.</summary>
    public DateTimeOffset UtcNow { get; init; }

    /// <summary>Admin feature is enabled on this process.</summary>
    public bool AdminEnabled { get; init; }
}

/// <summary>Invalidate request body.</summary>
public sealed class AdminInvalidateRequest
{
    /// <summary><c>domain</c>, <c>entity</c>, or <c>tags</c>.</summary>
    public string Scope { get; set; } = "domain";

    /// <summary>Domain name (required for domain/entity scopes).</summary>
    public string? Domain { get; set; }

    /// <summary>Entity id (required for entity scope).</summary>
    public string? EntityId { get; set; }

    /// <summary>Tags (required for tags scope).</summary>
    public string[]? Tags { get; set; }
}

/// <summary>Version set/bump request body.</summary>
public sealed class AdminVersionRequest
{
    /// <summary>
    /// New version token. When null or empty, the server generates a unique stamp.
    /// </summary>
    public string? Version { get; set; }
}

/// <summary>TTL patch request body (all fields optional).</summary>
public sealed class AdminTtlPatchRequest
{
    /// <summary>Output Cache TTL seconds.</summary>
    public int? OutputCacheTtlSeconds { get; set; }

    /// <summary>Fusion soft TTL seconds.</summary>
    public int? FusionCacheSoftTtlSeconds { get; set; }

    /// <summary>Fusion hard TTL seconds.</summary>
    public int? FusionCacheHardTtlSeconds { get; set; }

    /// <summary>Fusion fail-safe seconds.</summary>
    public int? FusionCacheFailSafeSeconds { get; set; }

    /// <summary>Client TTL seconds.</summary>
    public int? ClientTtlSeconds { get; set; }

    /// <summary>Client min TTL seconds.</summary>
    public int? ClientTtlMinSeconds { get; set; }
}

/// <summary>Response after version or TTL mutation.</summary>
public sealed class AdminDomainMutationResultDto
{
    /// <summary>Domain name.</summary>
    public required string Domain { get; init; }

    /// <summary>Effective configuration after the change.</summary>
    public required AdminDomainConfigDto Effective { get; init; }
}
