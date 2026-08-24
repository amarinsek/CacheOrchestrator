using CacheOrchestrator.Admin;

namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>
/// Cluster stats derived from Prometheus for a selected time window
/// (Prometheus-only; Local Admin process counters are not used).
/// </summary>
public sealed class WindowStatsDto
{
    /// <summary><see cref="MetricsStoreStatusCodes"/>.</summary>
    public required string Status { get; init; }

    /// <summary>Resolved range token or <c>custom</c>.</summary>
    public required string Range { get; init; }

    /// <summary>Window start (UTC).</summary>
    public DateTimeOffset FromUtc { get; init; }

    /// <summary>Window end (UTC).</summary>
    public DateTimeOffset ToUtc { get; init; }

    /// <summary>When the query finished.</summary>
    public DateTimeOffset QueriedAtUtc { get; init; }

    /// <summary>Error when not Connected.</summary>
    public string? Error { get; init; }

    /// <summary>Human scope note (e.g. Last 1 hour from Metrics store).</summary>
    public required string StatsWindow { get; init; }

    /// <summary>Cluster request sum in the window (OC outcomes preferred).</summary>
    public long TotalRequests { get; init; }

    /// <summary>Cluster invalidation sum in the window.</summary>
    public long TotalInvalidations { get; init; }

    /// <summary>Cluster OC hit share in the window.</summary>
    public double? OutputCacheHitShare { get; init; }

    /// <summary>Cluster DC hit share of requests in the window.</summary>
    public double? DataCacheHitShare { get; init; }

    /// <summary>Cluster factory share in the window.</summary>
    public double? FactoryShare { get; init; }

    /// <summary>Cluster pipeline shares.</summary>
    public AdminPipelineDto Pipeline { get; init; } = new();

    /// <summary>Cluster impact KPIs from window counters + factory duration samples.</summary>
    public CacheImpactKpiDto? Impact { get; init; }

    /// <summary>Domains aggregated in the window.</summary>
    public required IReadOnlyList<AdminDomainStatsDto> Domains { get; init; }

    /// <summary>Endpoints aggregated in the window (require route label).</summary>
    public required IReadOnlyList<AdminEndpointStatsDto> Endpoints { get; init; }

    /// <summary>
    /// True when Prometheus returned no counter samples for core OC series in the window.
    /// </summary>
    public bool NoData { get; init; }

    /// <summary>Hint summary over window domain/endpoint rows.</summary>
    public AdminHintSummaryDto HintSummary { get; init; } = new();
}
