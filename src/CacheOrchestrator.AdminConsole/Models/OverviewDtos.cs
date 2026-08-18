using CacheOrchestrator.Admin;

namespace CacheOrchestrator.AdminConsole.Models;

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
