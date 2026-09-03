using System.Diagnostics.Metrics;

namespace CacheOrchestrator.Edge.Diagnostics;

internal static class EdgeMetrics
{
    private static readonly Meter Meter = new("CacheOrchestrator.Edge");
    private static readonly Counter<long> Queued = Meter.CreateCounter<long>("cache_orchestrator.edge.invalidation.queued");
    private static readonly Counter<long> Purged = Meter.CreateCounter<long>("cache_orchestrator.edge.invalidation.keys");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("cache_orchestrator.edge.invalidation.failures");
    private static readonly Counter<long> Fallbacks = Meter.CreateCounter<long>("cache_orchestrator.edge.tags.fallback");

    public static void RecordQueued(string instance, string provider, int count) =>
        Queued.Add(count, new("instance", instance), new("provider", provider));

    public static void RecordPurged(string instance, string provider, int count) =>
        Purged.Add(count, new("instance", instance), new("provider", provider));

    public static void RecordFailure(string instance, string provider, string reason) =>
        Failures.Add(1, new("instance", instance), new("provider", provider), new("reason", reason));

    public static void RecordFallback(string domain, string provider) =>
        Fallbacks.Add(1, new("domain", domain), new("provider", provider), new("reason", "header_limit"));
}
