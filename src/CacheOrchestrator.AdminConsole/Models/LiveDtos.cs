using CacheOrchestrator.Admin;

namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>
/// Live (near-real-time) operational snapshot for the Live page.
/// Rates use a fixed short Prometheus lookback (default 1m), not the global Range picker.
/// Domain/endpoint/instance rows use the same DTOs as Overview window stats tables.
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

    /// <summary>Cluster pipeline shares projected from the live lookback (same panel as Overview).</summary>
    public AdminPipelineDto? Pipeline { get; init; }

    /// <summary>Configured instances with health + live request estimate when available.</summary>
    public required IReadOnlyList<InstanceStatusDto> Instances { get; init; }

    /// <summary>Domains with live traffic (hottest first before client sort).</summary>
    public required IReadOnlyList<AdminDomainStatsDto> Domains { get; init; }

    /// <summary>Endpoints with live traffic.</summary>
    public required IReadOnlyList<AdminEndpointStatsDto> Endpoints { get; init; }

    /// <summary>Configured domains with ≈0 RPS in the lookback (table-shaped).</summary>
    public IReadOnlyList<AdminDomainStatsDto> QuietDomains { get; init; } = [];

    /// <summary>
    /// Hint summary from HintEngine on synthetic stats projected from live rates + domain config.
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

/// <summary>Domain or endpoint live rate row (internal projection before Admin stats DTOs).</summary>
public sealed class LiveEntityRateDto
{
    /// <summary>Domain name or endpoint route key.</summary>
    public required string Name { get; set; }

    /// <summary>For endpoints: configured domain.</summary>
    public string? Domain { get; set; }

    public double RequestRate { get; set; }
    public double? OcHitShare { get; set; }
    public double? FcHitShare { get; set; }
    public double? FactoryShare { get; set; }
    public double? FactoryFailShare { get; set; }
}
