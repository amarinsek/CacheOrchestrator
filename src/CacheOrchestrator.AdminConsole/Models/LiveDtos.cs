using CacheOrchestrator.Admin;

namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>
/// Live (near-real-time) operational snapshot for the Live page.
/// Rates use a fixed short Prometheus lookback (default 1m), not the global Range picker.
/// </summary>
public sealed class LiveSnapshotDto
{
    /// <summary><see cref="MetricsStoreStatusCodes"/>.</summary>
    public required string Status { get; init; }

    /// <summary>Prometheus rate lookback (e.g. <c>1m</c>).</summary>
    public required string Lookback { get; init; }

    /// <summary>When the snapshot was built (UTC).</summary>
    public DateTimeOffset QueriedAtUtc { get; init; }

    /// <summary>Error when Metrics store is not usable.</summary>
    public string? Error { get; init; }

    /// <summary>Metrics store probe details.</summary>
    public MetricsStatusDto? Metrics { get; init; }

    /// <summary>Cluster-level live rates and shares.</summary>
    public required LiveClusterDto Cluster { get; init; }

    /// <summary>Configured instances with health + live RPS when available.</summary>
    public required IReadOnlyList<LiveInstanceDto> Instances { get; init; }

    /// <summary>Domains with live RPS (hottest first).</summary>
    public required IReadOnlyList<LiveEntityRateDto> Domains { get; init; }

    /// <summary>Top endpoints by live RPS.</summary>
    public required IReadOnlyList<LiveEntityRateDto> Endpoints { get; init; }

    /// <summary>Quiet configured domains (RPS ≈ 0) when config fan-out succeeded.</summary>
    public IReadOnlyList<string> QuietDomains { get; init; } = [];

    /// <summary>
    /// Hint summary from HintEngine on synthetic stats projected from live rates + domain config
    /// (no nested <c>/api/stats/window</c> Prom query set).
    /// </summary>
    public AdminHintSummaryDto HintSummary { get; init; } = new();
}

/// <summary>Cluster live KPIs.</summary>
public sealed class LiveClusterDto
{
    public int HealthyCount { get; init; }
    public int DegradedCount { get; init; }
    public int DownCount { get; init; }
    public int InstanceCount { get; init; }

    /// <summary>OC request rate (req/s) over the lookback.</summary>
    public double? RequestRate { get; init; }

    /// <summary>Factory (FC miss) rate (req/s).</summary>
    public double? FactoryRate { get; init; }

    /// <summary>Invalidation rate (1/s).</summary>
    public double? InvalidationRate { get; init; }

    public double? OcHitShare { get; init; }
    public double? FcHitShare { get; init; }
    public double? FactoryShare { get; init; }

    /// <summary>Share of requests with FC fail or stale in the lookback.</summary>
    public double? FactoryFailShare { get; init; }
}

/// <summary>Instance row for Live.</summary>
public sealed class LiveInstanceDto
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public required string Status { get; init; }
    public string? ReportedInstanceId { get; init; }
    public double? LatencyMs { get; init; }
    public long? UptimeSeconds { get; init; }
    public string? Error { get; init; }

    /// <summary>Live OC RPS attributed to scrape <c>instance_id</c> when present.</summary>
    public double? RequestRate { get; init; }
}

/// <summary>Domain or endpoint live rate row.</summary>
public sealed class LiveEntityRateDto
{
    /// <summary>Domain name or endpoint route key.</summary>
    public required string Name { get; init; }

    /// <summary>For endpoints: configured domain.</summary>
    public string? Domain { get; init; }

    public double RequestRate { get; init; }
    public double? OcHitShare { get; init; }
    public double? FcHitShare { get; init; }
    public double? FactoryShare { get; init; }
    public double? FactoryFailShare { get; init; }
}
