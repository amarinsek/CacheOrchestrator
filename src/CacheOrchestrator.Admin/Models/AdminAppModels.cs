using CacheOrchestrator.Admin;

namespace CacheOrchestrator.Admin.App.Models;

/// <summary>Health of a configured instance from the Admin App perspective.</summary>
public enum InstanceHealthStatus
{
    /// <summary>Local health endpoint responded successfully.</summary>
    Healthy = 0,

    /// <summary>Responded but reported problems or partial data.</summary>
    Degraded = 1,

    /// <summary>Unreachable or timed out.</summary>
    Down = 2
}

/// <summary>Instance row for Admin App listings.</summary>
public sealed class InstanceStatusDto
{
    /// <summary>Configured instance id.</summary>
    public required string Id { get; init; }

    /// <summary>Configured base URL.</summary>
    public required string Url { get; init; }

    /// <summary>Health classification.</summary>
    public InstanceHealthStatus Status { get; init; }

    /// <summary>Instance id reported by the Local API, if reachable.</summary>
    public string? ReportedInstanceId { get; init; }

    /// <summary>Error message when status is not Healthy.</summary>
    public string? Error { get; init; }

    /// <summary>Latency of the last health probe in milliseconds, if measured.</summary>
    public double? LatencyMs { get; init; }

    /// <summary>UTC process start time from Local Admin health, when known.</summary>
    public DateTimeOffset? StartedAtUtc { get; init; }

    /// <summary>Uptime in seconds from Local Admin health, when known.</summary>
    public long? UptimeSeconds { get; init; }

    /// <summary>Lifetime request count from the instance, when known.</summary>
    public long? Requests { get; init; }

    /// <summary>Aggregated recommendation hint counts for this instance (when available).</summary>
    public AdminHintSummaryDto? HintSummary { get; init; }
}

/// <summary>Generic fan-out result wrapper.</summary>
public sealed class FanOutResultDto<T>
{
    /// <summary>Aggregated or primary payload when applicable.</summary>
    public T? Data { get; init; }

    /// <summary>Per-instance outcomes.</summary>
    public required IReadOnlyList<InstanceCallResultDto> Results { get; init; }

    /// <summary>True when every targeted instance succeeded.</summary>
    public bool AllSucceeded => Results.Count > 0 && Results.All(r => r.Succeeded);

    /// <summary>True when at least one instance succeeded.</summary>
    public bool AnySucceeded => Results.Any(r => r.Succeeded);
}

/// <summary>Outcome of one Local Admin HTTP call.</summary>
public sealed class InstanceCallResultDto
{
    /// <summary>Configured instance id.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Whether the call succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>HTTP status code when a response was received.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Error or timeout message.</summary>
    public string? Error { get; init; }

    /// <summary>Call duration in milliseconds.</summary>
    public double LatencyMs { get; init; }
}

/// <summary>Compact overview for sticky header + Overview page.</summary>
public sealed class OverviewDto
{
    /// <summary>UTC generation time.</summary>
    public DateTimeOffset CollectedAtUtc { get; init; }

    /// <summary>Instance health rows.</summary>
    public required IReadOnlyList<InstanceStatusDto> Instances { get; init; }

    /// <summary>Healthy instance count.</summary>
    public int HealthyCount { get; init; }

    /// <summary>Degraded instance count.</summary>
    public int DegradedCount { get; init; }

    /// <summary>Down instance count.</summary>
    public int DownCount { get; init; }

    /// <summary>Sum of requests across cluster.</summary>
    public long TotalRequests { get; init; }

    /// <summary>Sum of invalidations.</summary>
    public long TotalInvalidations { get; init; }

    /// <summary>Cluster-weighted pipeline shares.</summary>
    public AdminPipelineDto Pipeline { get; init; } = new();

    /// <summary>Cluster OC hit share.</summary>
    public double? OcHitShare { get; init; }

    /// <summary>Cluster origin share.</summary>
    public double? OriginShare { get; init; }

    /// <summary>Human-readable warnings.</summary>
    public IReadOnlyList<string> Alerts { get; init; } = [];

    /// <summary>
    /// All aggregated domains for Overview. UI sorts by the selected key, then shows top 5.
    /// </summary>
    public IReadOnlyList<AdminDomainStatsDto> TopDomains { get; init; } = [];

    /// <summary>
    /// All aggregated endpoints for Overview. UI sorts by the selected key, then shows top 5.
    /// </summary>
    public IReadOnlyList<AdminEndpointStatsDto> TopEndpoints { get; init; } = [];

    /// <summary>Domain count with traffic or config.</summary>
    public int DomainCount { get; init; }

    /// <summary>Endpoint count observed.</summary>
    public int EndpointCount { get; init; }

    /// <summary>Cluster-wide aggregated recommendation hints.</summary>
    public AdminHintSummaryDto HintSummary { get; init; } = new();

    /// <summary>Distinct top hint messages for overview (capped).</summary>
    public IReadOnlyList<AdminHintDto> TopHints { get; init; } = [];
}

/// <summary>Cluster (or single-instance) aggregated live stats.</summary>
public sealed class ClusterStatsDto
{
    /// <summary>Scope label: <c>all</c> or <c>instance:{id}</c>.</summary>
    public required string Scope { get; init; }

    /// <summary>Whether per-instance breakdowns are included.</summary>
    public bool GroupByInstance { get; init; }

    /// <summary>UTC collection time on the Admin App.</summary>
    public DateTimeOffset CollectedAtUtc { get; init; }

    /// <summary>Per-instance raw snapshots that contributed.</summary>
    public required IReadOnlyList<InstanceStatsContributionDto> Instances { get; init; }

    /// <summary>Domains aggregated across contributing instances.</summary>
    public required IReadOnlyList<AdminDomainStatsDto> Domains { get; init; }

    /// <summary>
    /// Endpoints as fundamental unit (cluster merge of all routes).
    /// Prefer this over nested domain.endpoints for EP-first views.
    /// </summary>
    public required IReadOnlyList<AdminEndpointStatsDto> Endpoints { get; init; }

    /// <summary>Endpoints without a domain after merge.</summary>
    public required IReadOnlyList<AdminEndpointStatsDto> UnassignedEndpoints { get; init; }
}

/// <summary>One instance's contribution to cluster stats.</summary>
public sealed class InstanceStatsContributionDto
{
    /// <summary>Configured instance id.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Whether stats were obtained.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Error when failed.</summary>
    public string? Error { get; init; }

    /// <summary>Local snapshot when succeeded.</summary>
    public AdminLiveStatsSnapshot? Snapshot { get; init; }
}

/// <summary>Invalidate request for the Admin App (adds multi-instance target).</summary>
public sealed class AdminAppInvalidateRequest
{
    /// <summary><c>domain</c>, <c>entity</c>, or <c>tags</c>.</summary>
    public string Scope { get; set; } = "domain";

    /// <summary>Domain name.</summary>
    public string? Domain { get; set; }

    /// <summary>Entity id.</summary>
    public string? EntityId { get; set; }

    /// <summary>Custom tags.</summary>
    public string[]? Tags { get; set; }

    /// <summary><c>all</c> or <c>instance:{id}</c>.</summary>
    public string Target { get; set; } = "all";
}

/// <summary>Version request with multi-instance target.</summary>
public sealed class AdminAppVersionRequest
{
    /// <summary>New version token; empty generates a stamp on each instance.</summary>
    public string? Version { get; set; }

    /// <summary><c>all</c> or <c>instance:{id}</c>.</summary>
    public string Target { get; set; } = "all";
}

/// <summary>TTL patch with multi-instance target.</summary>
public sealed class AdminAppTtlPatchRequest
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

    /// <summary><c>all</c> or <c>instance:{id}</c>.</summary>
    public string Target { get; set; } = "all";
}
