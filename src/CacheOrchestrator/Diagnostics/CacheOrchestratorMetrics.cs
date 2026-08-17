using System.Diagnostics;
using System.Diagnostics.Metrics;
using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Invalidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Diagnostics;

/// <summary>
/// Domain-level metrics for CacheOrchestrator.
/// Zero meaningful overhead when no MeterListener / OpenTelemetry is subscribed.
/// Optional stable <c>route</c> tag when <see cref="CacheOrchestratorOptions.MetricsOptions.IncludeEndpointLabel"/> is true.
/// </summary>
public static class CacheOrchestratorMetrics
{
    /// <summary>Meter name (subscribe with this value).</summary>
    public const string MeterName = "CacheOrchestrator";

    /// <summary>Prometheus / OTel tag for the stable endpoint key when enabled.</summary>
    public const string RouteTagName = "route";

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
            description: "Fusion GetOrSet duration in milliseconds (legacy; all results with a duration). Prefer factory.duration for factory cost.");

    private static readonly Histogram<double> FactoryDurationMs =
        Meter.CreateHistogram<double>(
            "cache_orchestrator.factory.duration",
            unit: "ms",
            description: "Value factory wall time in milliseconds (miss/stale path only)");

    private static readonly Histogram<double> FactoryResultSizeBytes =
        Meter.CreateHistogram<double>(
            "cache_orchestrator.factory.result_size",
            unit: "By",
            description: "Value factory result size in bytes when cheaply measurable (miss path)");

    private static readonly Counter<long> ClientSchedule =
        Meter.CreateCounter<long>(
            "cache_orchestrator.client.schedule",
            unit: "{response}",
            description: "Client Cache Schedule phase applied to Cache-Control by domain");

    private static readonly Counter<long> ClusterPublished =
        Meter.CreateCounter<long>(
            "cache_orchestrator.cluster.commands_published",
            unit: "{command}",
            description: "Cluster commands published by this instance");

    private static readonly Counter<long> ClusterReceived =
        Meter.CreateCounter<long>(
            "cache_orchestrator.cluster.commands_received",
            unit: "{command}",
            description: "Cluster commands received by this instance");

    private static readonly Counter<long> ClusterApplied =
        Meter.CreateCounter<long>(
            "cache_orchestrator.cluster.commands_applied",
            unit: "{command}",
            description: "Cluster commands successfully applied locally");

    private static readonly Counter<long> ClusterPublishFailures =
        Meter.CreateCounter<long>(
            "cache_orchestrator.cluster.publish_failures",
            unit: "{failure}",
            description: "Cluster peer publish failures (timeout, HTTP error, transport)");

    private static readonly Counter<long> ClusterDedupeHits =
        Meter.CreateCounter<long>(
            "cache_orchestrator.cluster.command_dedupe_hits",
            unit: "{command}",
            description: "Cluster commands ignored as duplicates within the dedupe window");

    /// <summary>
    /// When <c>Cache:Metrics:IncludeEndpointLabel</c> is true, returns the stable endpoint key
    /// (<c>METHOD pattern</c>, same as Admin). Otherwise null without building a key.
    /// Uses <see cref="AdminEndpointKey.TryGet"/> (per-request cache).
    /// </summary>
    public static string? TryGetEndpointRouteLabel(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!IsEndpointLabelEnabled(http))
            return null;
        string? key = AdminEndpointKey.TryGet(http);
        return string.IsNullOrEmpty(key) ? null : key;
    }

    /// <summary>
    /// Resolves Admin endpoint key and optional metrics route in one pass.
    /// </summary>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="forAdminStats">When true, always resolve the key for Local Admin counters.</param>
    /// <param name="endpointKey">Admin counter key when <paramref name="forAdminStats"/> is true.</param>
    /// <param name="metricsRoute">Route tag when IncludeEndpointLabel is enabled.</param>
    internal static void ResolveEndpointKeys(
        HttpContext http,
        bool forAdminStats,
        out string? endpointKey,
        out string? metricsRoute)
    {
        endpointKey = null;
        metricsRoute = null;
        bool includeRoute = IsEndpointLabelEnabled(http);
        if (!forAdminStats && !includeRoute)
            return;

        string? key = AdminEndpointKey.TryGet(http);
        if (string.IsNullOrEmpty(key))
            return;

        if (forAdminStats)
            endpointKey = key;
        if (includeRoute)
            metricsRoute = key;
    }

    private static bool IsEndpointLabelEnabled(HttpContext http) =>
        http.RequestServices?.GetService<IOptions<CacheOrchestratorOptions>>()?.Value
            is { Metrics.IncludeEndpointLabel: true };

    /// <summary>
    /// Records a FusionCache operation outcome (and optional duration).
    /// </summary>
    /// <param name="domain">Domain name.</param>
    /// <param name="result">Result code: hit, miss, stale, fail, bypass, off.</param>
    /// <param name="durationMs">Optional duration in milliseconds.</param>
    /// <param name="route">Optional stable endpoint key when IncludeEndpointLabel is enabled.</param>
    /// <param name="resultSizeBytes">Optional measured factory result size (bytes) on miss.</param>
    internal static void RecordFusion(
        string domain,
        string result,
        double? durationMs = null,
        string? route = null,
        long? resultSizeBytes = null)
    {
        TagList tags = BuildDomainResultTags(domain, result, route);
        FcRequests.Add(1, tags);

        if (durationMs is double ms)
        {
            // Legacy: all timed GetOrSet outcomes (dashboards may still use this).
            FcDurationMs.Record(ms, tags);

            // Canonical factory cost: factory ran (miss, fail-safe stale, or hard fail).
            if (result is "miss" or "stale" or "fail")
                FactoryDurationMs.Record(ms, tags);
        }

        if (resultSizeBytes is long size && size >= 0 && result is "miss")
            FactoryResultSizeBytes.Record(size, tags);
    }

    /// <summary>
    /// Records an Output Cache outcome.
    /// </summary>
    /// <param name="domain">Domain name.</param>
    /// <param name="result">Result code: hit, miss, bypass.</param>
    /// <param name="route">Optional stable endpoint key when IncludeEndpointLabel is enabled.</param>
    internal static void RecordOutput(string domain, string result, string? route = null) =>
        OcRequests.Add(1, BuildDomainResultTags(domain, result, route));

    /// <summary>
    /// Records a successful invalidation attributed to a <strong>domain</strong> only.
    /// Do not pass entity paths (e.g. <c>product-crud/products/42</c>) — that explodes series cardinality.
    /// </summary>
    /// <param name="domain">Normalized domain name (not entity id).</param>
    /// <param name="kind">Optional invalidation kind for low-cardinality breakdown (Domain, Entity, EntityKind).</param>
    internal static void RecordInvalidate(string domain, CacheInvalidationKind? kind = null)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Contains('/', StringComparison.Ordinal))
            return; // refuse high-cardinality / path-like labels

        if (kind is null)
        {
            Invalidations.Add(1, new KeyValuePair<string, object?>("domain", domain));
            return;
        }

        Invalidations.Add(
            1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("kind", kind.Value.ToString()));
    }

    /// <summary>
    /// Records the Client Cache Schedule phase applied when writing <c>Cache-Control</c>.
    /// </summary>
    /// <param name="domain">Domain name.</param>
    /// <param name="phase">Phase wire value (e.g. calm, approaching, hold, n/a).</param>
    internal static void RecordClientSchedule(string domain, string phase) =>
        ClientSchedule.Add(1, new KeyValuePair<string, object?>("domain", domain), new KeyValuePair<string, object?>("phase", phase));

    /// <summary>Records a successful origin publish attempt to the cluster bus.</summary>
    internal static void RecordClusterPublished(string commandType) =>
        ClusterPublished.Add(1, new KeyValuePair<string, object?>("command_type", commandType));

    /// <summary>Records that a peer delivery request was accepted for apply handling.</summary>
    internal static void RecordClusterReceived(string commandType) =>
        ClusterReceived.Add(1, new KeyValuePair<string, object?>("command_type", commandType));

    /// <summary>Records local ApplyLocal success for a cluster command.</summary>
    internal static void RecordClusterApplied(string commandType) =>
        ClusterApplied.Add(1, new KeyValuePair<string, object?>("command_type", commandType));

    /// <summary>Records a per-peer publish failure.</summary>
    internal static void RecordClusterPublishFailure(string reason) =>
        ClusterPublishFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Records a receive-side CommandId dedupe hit.</summary>
    internal static void RecordClusterDedupeHit() =>
        ClusterDedupeHits.Add(1);

    private static TagList BuildDomainResultTags(string domain, string result, string? route)
    {
        TagList tags = new()
        {
            { "domain", domain },
            { "result", result },
        };
        if (!string.IsNullOrEmpty(route))
            tags.Add(RouteTagName, route);
        return tags;
    }
}
