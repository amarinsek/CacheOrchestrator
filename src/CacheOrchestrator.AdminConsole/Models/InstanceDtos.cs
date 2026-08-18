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
