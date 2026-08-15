using CacheOrchestrator.AdminConsole.Models;

namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>Low-level client for a Prometheus-compatible HTTP API.</summary>
public interface IMetricsQueryClient
{
    /// <summary>Probes readiness / buildinfo. Does not throw for HTTP failures.</summary>
    Task<MetricsProbeResult> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>/api/v1/query_range</c>. Throws <see cref="InvalidOperationException"/> when not configured.
    /// </summary>
    Task<IReadOnlyList<PrometheusMatrixSeries>> QueryRangeAsync(
        string promQl,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string step,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>/api/v1/query</c> (instant). Throws <see cref="InvalidOperationException"/> when not configured.
    /// </summary>
    Task<IReadOnlyList<PrometheusInstantSample>> QueryInstantAsync(
        string promQl,
        DateTimeOffset? timeUtc = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a connectivity probe.</summary>
public sealed class MetricsProbeResult
{
    public bool Succeeded { get; init; }
    public double? LatencyMs { get; init; }
    public string? Error { get; init; }
}

/// <summary>One matrix series from query_range.</summary>
public sealed class PrometheusMatrixSeries
{
    public required IReadOnlyDictionary<string, string> Metric { get; init; }
    public required IReadOnlyList<MetricsPointDto> Points { get; init; }
}

/// <summary>One instant sample from query.</summary>
public sealed class PrometheusInstantSample
{
    public required IReadOnlyDictionary<string, string> Metric { get; init; }
    public double? Value { get; init; }
    public long? TimestampUnix { get; init; }
}
