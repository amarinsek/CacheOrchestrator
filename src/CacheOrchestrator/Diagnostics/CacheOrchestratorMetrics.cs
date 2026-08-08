using System.Diagnostics.Metrics;

namespace CacheOrchestrator.Diagnostics;

/// <summary>
/// Domain-level metrics for CacheOrchestrator.
/// Zero meaningful overhead when no MeterListener / OpenTelemetry is subscribed.
/// </summary>
public static class CacheOrchestratorMetrics
{
    /// <summary>Meter name (subscribe with this value).</summary>
    public const string MeterName = "CacheOrchestrator";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> FcRequests =
        Meter.CreateCounter<long>(
            "cache_orchestrator.fc.requests",
            unit: "{request}",
            description: "Fusion cache operations by domain and result");

    private static readonly Counter<long> OcRequests =
        Meter.CreateCounter<long>(
            "cache_orchestrator.oc.requests",
            unit: "{request}",
            description: "Output cache outcomes by domain and result");

    private static readonly Counter<long> Invalidations =
        Meter.CreateCounter<long>(
            "cache_orchestrator.invalidate",
            unit: "{invalidation}",
            description: "Domain invalidation calls");

    private static readonly Histogram<double> FcDurationMs =
        Meter.CreateHistogram<double>(
            "cache_orchestrator.fc.duration",
            unit: "ms",
            description: "Fusion GetOrSet duration in milliseconds");

    private static readonly Counter<long> ClientSchedule =
        Meter.CreateCounter<long>(
            "cache_orchestrator.client.schedule",
            unit: "{response}",
            description: "Client Cache Schedule phase applied to Cache-Control by domain");

    /// <summary>
    /// Records a FusionCache operation outcome (and optional duration).
    /// </summary>
    /// <param name="domain">Domain name.</param>
    /// <param name="result">Result code: hit, miss, stale, bypass, off.</param>
    /// <param name="durationMs">Optional duration in milliseconds.</param>
    internal static void RecordFusion(string domain, string result, double? durationMs = null)
    {
        FcRequests.Add(1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("result", result));

        if (durationMs is double ms)
        {
            FcDurationMs.Record(ms,
                new KeyValuePair<string, object?>("domain", domain),
                new KeyValuePair<string, object?>("result", result));
        }
    }

    /// <summary>
    /// Records an Output Cache outcome.
    /// </summary>
    /// <param name="domain">Domain name.</param>
    /// <param name="result">Result code: hit, miss, bypass.</param>
    internal static void RecordOutput(string domain, string result) =>
        OcRequests.Add(1, new KeyValuePair<string, object?>("domain", domain), new KeyValuePair<string, object?>("result", result));

    /// <summary>
    /// Records a successful domain invalidation.
    /// </summary>
    /// <param name="domain">Domain name.</param>
    internal static void RecordInvalidate(string domain) =>
        Invalidations.Add(1, new KeyValuePair<string, object?>("domain", domain));

    /// <summary>
    /// Records the Client Cache Schedule phase applied when writing <c>Cache-Control</c>.
    /// </summary>
    /// <param name="domain">Domain name.</param>
    /// <param name="phase">Phase wire value (e.g. calm, approaching, hold, n/a).</param>
    internal static void RecordClientSchedule(string domain, string phase) =>
        ClientSchedule.Add(1, new KeyValuePair<string, object?>("domain", domain), new KeyValuePair<string, object?>("phase", phase));
}