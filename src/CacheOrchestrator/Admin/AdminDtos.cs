namespace CacheOrchestrator.Admin;

/// <summary>OC layer counters with layer rates and request shares.</summary>
public sealed class AdminLayerDto
{
    /// <summary>Cache hits.</summary>
    public long Hits { get; init; }

    /// <summary>Cache misses.</summary>
    public long Misses { get; init; }

    /// <summary>Bypassed requests (not counted in layer hit rate).</summary>
    public long Bypass { get; init; }

    /// <summary><c>hits + misses</c> — sample size for layer rates.</summary>
    public long LayerSampleSize { get; init; }

    /// <summary>Layer rate: <c>hits / (hits + misses)</c>.</summary>
    public double? HitRate { get; init; }

    /// <summary>Layer rate: <c>misses / (hits + misses)</c>.</summary>
    public double? MissRate { get; init; }

    /// <summary>Request share: <c>hits / requests</c>.</summary>
    public double? HitShare { get; init; }

    /// <summary>Request share: <c>misses / requests</c>.</summary>
    public double? MissShare { get; init; }

    /// <summary>Request share: <c>bypass / requests</c>.</summary>
    public double? BypassShare { get; init; }

    /// <summary>True when layer sample is positive but below <see cref="AdminStatsMath.LowSampleThreshold"/>.</summary>
    public bool LowSample { get; init; }
}

/// <summary>Fusion layer counters with layer rates and request shares.</summary>
public sealed class AdminFusionLayerDto
{
    /// <summary>Cache hits.</summary>
    public long Hits { get; init; }

    /// <summary>Cache misses (factory ran).</summary>
    public long Misses { get; init; }

    /// <summary>Bypassed requests.</summary>
    public long Bypass { get; init; }

    /// <summary>Fail-safe stale serves.</summary>
    public long Stale { get; init; }

    /// <summary>Times the value factory ran.</summary>
    public long FactoryRuns { get; init; }

    /// <summary>Times the value factory threw.</summary>
    public long FactoryFailures { get; init; }

    /// <summary><c>hits + misses</c> for layer rates.</summary>
    public long LayerSampleSize { get; init; }

    /// <summary>Layer rate: hits / (hits + misses).</summary>
    public double? HitRate { get; init; }

    /// <summary>Layer rate: misses / (hits + misses).</summary>
    public double? MissRate { get; init; }

    /// <summary>Stale / (hits + misses + stale).</summary>
    public double? StaleRate { get; init; }

    /// <summary>Request share: hits / requests.</summary>
    public double? HitShare { get; init; }

    /// <summary>Request share: misses / requests.</summary>
    public double? MissShare { get; init; }

    /// <summary>Request share: stale / requests.</summary>
    public double? StaleShare { get; init; }

    /// <summary>Request share: bypass / requests.</summary>
    public double? BypassShare { get; init; }

    /// <summary>Request share: factoryRuns / requests (origin load).</summary>
    public double? OriginShare { get; init; }

    /// <summary>True when layer sample is positive but low.</summary>
    public bool LowSample { get; init; }
}

/// <summary>
/// Approximate pipeline breakdown of requests (shares of the same denominator).
/// OC hit and FC paths are mutually exclusive for a given request when OC serves first.
/// </summary>
public sealed class AdminPipelineDto
{
    /// <summary>Served entirely from Output Cache.</summary>
    public double? OcHitShare { get; init; }

    /// <summary>Served from Fusion without factory.</summary>
    public double? FcHitShare { get; init; }

    /// <summary>Origin / factory share.</summary>
    public double? OriginShare { get; init; }

    /// <summary>OC or FC bypass share (combined).</summary>
    public double? BypassShare { get; init; }

    /// <summary>Remainder (e.g. OC miss accounted via FC, rounding).</summary>
    public double? OtherShare { get; init; }
}

/// <summary>Rule-based operational hint (recommendations engine).</summary>
public sealed class AdminHintDto
{
    /// <summary><c>Info</c>, <c>Warning</c>, or <c>Critical</c>.</summary>
    public required string Severity { get; init; }

    /// <summary>Stable machine-readable code (e.g. <c>low-fc-hit-rate</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable suggestion.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Aggregated hint counts for cluster / instance header chips.
/// Highest severity present drives urgency styling.
/// </summary>
public sealed class AdminHintSummaryDto
{
    /// <summary>Info count.</summary>
    public int Info { get; init; }

    /// <summary>Warning count.</summary>
    public int Warning { get; init; }

    /// <summary>Critical count.</summary>
    public int Critical { get; init; }

    /// <summary>Total hints.</summary>
    public int Total => Info + Warning + Critical;

    /// <summary>Highest severity present: Critical, Warning, Info, or None.</summary>
    public string MaxSeverity =>
        Critical > 0 ? "Critical" : Warning > 0 ? "Warning" : Info > 0 ? "Info" : "None";
}

/// <summary>Spread of a ratio across instances (heterogeneity signal).</summary>
public sealed class AdminShareSpreadDto
{
    /// <summary>Minimum observed ratio.</summary>
    public double? Min { get; init; }

    /// <summary>Maximum observed ratio.</summary>
    public double? Max { get; init; }

    /// <summary>Arithmetic mean across instances that reported a value.</summary>
    public double? Mean { get; init; }

    /// <summary>Population stdev; null when fewer than 2 samples.</summary>
    public double? Stdev { get; init; }

    /// <summary>Number of instance values included.</summary>
    public int SampleCount { get; init; }
}

/// <summary>Optional per-metric instance spreads for a cluster aggregate row.</summary>
public sealed class AdminInstanceSpreadDto
{
    /// <summary>OC hit share across instances.</summary>
    public AdminShareSpreadDto? OcHitShare { get; init; }

    /// <summary>FC hit share across instances.</summary>
    public AdminShareSpreadDto? FcHitShare { get; init; }

    /// <summary>FC miss share across instances.</summary>
    public AdminShareSpreadDto? FcMissShare { get; init; }

    /// <summary>Origin share across instances.</summary>
    public AdminShareSpreadDto? OriginShare { get; init; }

    /// <summary>OC layer hit rate across instances.</summary>
    public AdminShareSpreadDto? OcHitRate { get; init; }

    /// <summary>FC layer hit rate across instances.</summary>
    public AdminShareSpreadDto? FcHitRate { get; init; }
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

    /// <summary>Endpoints without a known domain.</summary>
    public required IReadOnlyList<AdminEndpointStatsDto> UnassignedEndpoints { get; init; }

    /// <summary>
    /// Flat endpoint list (all domains + unassigned) for EP-first consumers.
    /// </summary>
    public IReadOnlyList<AdminEndpointStatsDto> Endpoints { get; init; } = [];
}

/// <summary>Domain-level live stats.</summary>
public sealed class AdminDomainStatsDto
{
    /// <summary>Normalized domain name.</summary>
    public required string Name { get; init; }

    /// <summary>Instance id when this row is instance-scoped; null for aggregates.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Effective Version.</summary>
    public required string Version { get; init; }

    /// <summary>True when Version is a runtime override.</summary>
    public bool VersionIsRuntimeOverride { get; init; }

    /// <summary>Client Cache Schedule phase wire value.</summary>
    public string? SchedulePhase { get; init; }

    /// <summary>Last successful invalidation UTC.</summary>
    public DateTimeOffset? LastInvalidationUtc { get; init; }

    /// <summary>Successful invalidation count (process lifetime on instance; sum on cluster).</summary>
    public long Invalidations { get; init; }

    /// <summary>Request denominator for shares.</summary>
    public long Requests { get; init; }

    /// <summary>Output Cache counters + rates/shares.</summary>
    public required AdminLayerDto Oc { get; init; }

    /// <summary>FusionCache counters + rates/shares.</summary>
    public required AdminFusionLayerDto Fc { get; init; }

    /// <summary>Pipeline shares of requests.</summary>
    public AdminPipelineDto Pipeline { get; init; } = new();

    /// <summary>Endpoints attributed to this domain.</summary>
    public IReadOnlyList<AdminEndpointStatsDto> Endpoints { get; init; } = [];

    /// <summary>Per-instance rows when cluster view groups by instance.</summary>
    public IReadOnlyList<AdminDomainStatsDto>? ByInstance { get; init; }

    /// <summary>Spread of key shares/rates across instances (cluster only).</summary>
    public AdminInstanceSpreadDto? InstanceSpread { get; init; }

    /// <summary>Rule-based recommendations for this domain.</summary>
    public IReadOnlyList<AdminHintDto> Hints { get; init; } = [];
}

/// <summary>Endpoint-level live stats (fundamental unit).</summary>
public sealed class AdminEndpointStatsDto
{
    /// <summary>e.g. <c>GET /api/products/{id}</c>.</summary>
    public required string Route { get; init; }

    /// <summary>Instance id when row is instance-scoped.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Configured or runtime domain, if known.</summary>
    public string? ConfiguredDomain { get; init; }

    /// <summary>Request denominator for shares.</summary>
    public long Requests { get; init; }

    /// <summary>Output Cache layer.</summary>
    public required AdminLayerDto Oc { get; init; }

    /// <summary>Fusion layer.</summary>
    public required AdminFusionLayerDto Fc { get; init; }

    /// <summary>Pipeline shares.</summary>
    public AdminPipelineDto Pipeline { get; init; } = new();

    /// <summary>Per-instance breakdown when clustered.</summary>
    public IReadOnlyList<AdminEndpointStatsDto>? ByInstance { get; init; }

    /// <summary>Spread across instances.</summary>
    public AdminInstanceSpreadDto? InstanceSpread { get; init; }

    /// <summary>Rule-based recommendations for this endpoint.</summary>
    public IReadOnlyList<AdminHintDto> Hints { get; init; } = [];
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

    /// <summary>UTC now on the instance.</summary>
    public DateTimeOffset UtcNow { get; init; }

    /// <summary>Admin feature is enabled on this process.</summary>
    public bool AdminEnabled { get; init; }

    /// <summary>UTC process start time (host process).</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>Elapsed time since <see cref="StartedAtUtc"/> in whole seconds.</summary>
    public long UptimeSeconds { get; init; }

    /// <summary>
    /// Lifetime request count on this process (from Admin live counters), when available.
    /// </summary>
    public long Requests { get; init; }
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
