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

/// <summary>Cluster (or single-instance) aggregated live stats.</summary>
public sealed class ClusterStatsDto
{
    /// <summary>Scope label: <c>all</c> or <c>instance:{id}</c>.</summary>
    public required string Scope { get; init; }

    /// <summary>UTC collection time on the Admin App.</summary>
    public DateTimeOffset CollectedAtUtc { get; init; }

    /// <summary>Per-instance raw snapshots that contributed.</summary>
    public required IReadOnlyList<InstanceStatsContributionDto> Instances { get; init; }

    /// <summary>Domains aggregated across contributing instances.</summary>
    public required IReadOnlyList<AdminDomainStatsDto> Domains { get; init; }

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
