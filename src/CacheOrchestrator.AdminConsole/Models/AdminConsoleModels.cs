using CacheOrchestrator.Admin;

namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>Health of a configured instance from the Admin Console App perspective.</summary>
public enum InstanceHealthStatus
{
    /// <summary>Local health endpoint responded successfully.</summary>
    Healthy = 0,

    /// <summary>Responded but reported problems or partial data.</summary>
    Degraded = 1,

    /// <summary>Unreachable or timed out.</summary>
    Down = 2
}

/// <summary>Instance row for Admin Console App listings.</summary>
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

/// <summary>How a write operation was delivered to instances.</summary>
public static class DistributionModes
{
    /// <summary>HTTP call to each targeted instance with <c>distribute: false</c>.</summary>
    public const string FanOut = "fan-out";

    /// <summary>Single origin with <c>distribute: true</c>; peers receive via cluster bus.</summary>
    public const string BusDistribute = "bus-distribute";
}

/// <summary>Generic fan-out result wrapper.</summary>
public sealed class FanOutResultDto<T>
{
    /// <summary>Aggregated or primary payload when applicable.</summary>
    public T? Data { get; init; }

    /// <summary>Per-instance outcomes (Admin Console App HTTP targets only).</summary>
    public required IReadOnlyList<InstanceCallResultDto> Results { get; init; }

    /// <summary>
    /// <see cref="DistributionModes.FanOut"/> or <see cref="DistributionModes.BusDistribute"/>.
    /// </summary>
    public string DistributionMode { get; init; } = DistributionModes.FanOut;

    /// <summary>Human-readable summary for UI (how peers were reached).</summary>
    public string? DistributionSummary { get; init; }

    /// <summary>When bus-distribute: the single origin instance id contacted by Admin Console App.</summary>
    public string? BusOriginInstanceId { get; init; }

    /// <summary>Whether Local Admin requests used <c>distribute: true</c>.</summary>
    public bool Distribute { get; init; }

    /// <summary>True when every targeted instance succeeded.</summary>
    public bool AllSucceeded => Results.Count > 0 && Results.All(r => r.Succeeded);

    /// <summary>True when at least one instance succeeded.</summary>
    public bool AnySucceeded => Results.Any(r => r.Succeeded);
}

/// <summary>Cluster bus capability snapshot from Local <c>GET …/cluster/info</c>.</summary>
public sealed class LocalClusterInfoDto
{
    /// <summary>Process instance id.</summary>
    public string? InstanceId { get; set; }

    /// <summary>Cache namespace.</summary>
    public string? Namespace { get; set; }

    /// <summary>Whether the HTTP bus is enabled on that process.</summary>
    public bool BusEnabled { get; set; }

    /// <summary>Membership kind (Null, Static, ServiceDiscovery).</summary>
    public string? Membership { get; set; }

    /// <summary>Known peer count from membership.</summary>
    public int PeerCount { get; set; }
}

/// <summary>Aggregated cluster distribution capability for Admin Console App UI.</summary>
public sealed class ClusterDistributionCapabilityDto
{
    /// <summary>Recommended mode for writes when target is <c>all</c>.</summary>
    public required string RecommendedMode { get; init; }

    /// <summary>UI summary.</summary>
    public required string Summary { get; init; }

    /// <summary>True when at least one healthy instance reports an enabled non-null bus.</summary>
    public bool BusAvailable { get; init; }

    /// <summary>Preferred origin for bus-distribute (first healthy bus-enabled instance).</summary>
    public string? PreferredBusOriginId { get; init; }

    /// <summary>Per-instance cluster probes.</summary>
    public required IReadOnlyList<InstanceClusterProbeDto> Instances { get; init; }
}

/// <summary>One instance's cluster probe for Admin Console App.</summary>
public sealed class InstanceClusterProbeDto
{
    /// <summary>Configured instance id.</summary>
    public required string Id { get; init; }

    /// <summary>Whether the cluster/info call succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Whether bus is enabled on that instance.</summary>
    public bool BusEnabled { get; init; }

    /// <summary>Membership kind when known.</summary>
    public string? Membership { get; set; }

    /// <summary>Peer count when known.</summary>
    public int? PeerCount { get; set; }

    /// <summary>Error when probe failed.</summary>
    public string? Error { get; set; }
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

    private double? _factoryShare;

    /// <summary>Cluster factory share (also known as origin share).</summary>
    public double? FactoryShare
    {
        get => _factoryShare;
        init => _factoryShare = value;
    }

    /// <summary>
    /// Obsolete synonym for <see cref="FactoryShare"/> (JSON <c>originShare</c>).
    /// Prefer <see cref="FactoryShare"/>.
    /// </summary>
    [Obsolete("Use FactoryShare. OriginShare remains for JSON/wire compatibility.")]
    public double? OriginShare
    {
        get => _factoryShare;
        init => _factoryShare = value;
    }

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

    /// <summary>Cluster-level impact KPIs (from summed domain counters; process lifetime).</summary>
    public CacheImpactKpiDto? Impact { get; init; }

    /// <summary>
    /// Label for counter window, e.g. <c>since process start</c>.
    /// </summary>
    public string StatsWindow { get; init; } = "since process start";

    /// <summary>
    /// Impact over the last Admin Console poll interval (delta of lifetime counters), when available.
    /// </summary>
    public CacheImpactKpiDto? ImpactRecent { get; init; }

    /// <summary>Label for <see cref="ImpactRecent"/> (e.g. last ~15s poll delta).</summary>
    public string? RecentWindowLabel { get; init; }
}

/// <summary>Invalidate request for the Admin Console App (adds multi-instance target).</summary>
public sealed class AdminConsoleInvalidateRequest
{
    /// <summary><c>domain</c>, <c>entity</c>, <c>entityKind</c>, or <c>tags</c>.</summary>
    public string Scope { get; set; } = "domain";

    /// <summary>Domain name.</summary>
    public string? Domain { get; set; }

    /// <summary>Entity kind (required for entity / entityKind scopes).</summary>
    public string? EntityKind { get; set; }

    /// <summary>Entity id.</summary>
    public string? EntityId { get; set; }

    /// <summary>Custom tags.</summary>
    public string[]? Tags { get; set; }

    /// <summary><c>all</c> or <c>instance:{id}</c>.</summary>
    public string Target { get; set; } = "all";
}

/// <summary>Version request with multi-instance target.</summary>
public sealed class AdminConsoleVersionRequest
{
    /// <summary>New version token; empty generates a stamp on each instance.</summary>
    public string? Version { get; set; }

    /// <summary><c>all</c> or <c>instance:{id}</c>.</summary>
    public string Target { get; set; } = "all";
}

/// <summary>TTL patch with multi-instance target.</summary>
public sealed class AdminConsoleTtlPatchRequest
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
