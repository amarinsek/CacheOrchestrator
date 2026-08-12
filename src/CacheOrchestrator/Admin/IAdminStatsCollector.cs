namespace CacheOrchestrator.Admin;

/// <summary>
/// Live in-process counters for Local Admin API. No-op when Admin is disabled.
/// </summary>
public interface IAdminStatsCollector
{
    /// <summary>When false, callers should skip recording (hot path).</summary>
    bool IsEnabled { get; }

    /// <summary>Whether per-endpoint counters are maintained.</summary>
    bool TrackEndpoints { get; }

    /// <summary>Whether factory latency sum/count is tracked.</summary>
    bool TrackLatency { get; }

    /// <summary>
    /// Records an Output Cache outcome.
    /// </summary>
    /// <param name="endpointKey">e.g. <c>GET /api/x</c>, or null.</param>
    /// <param name="domain">Normalized domain, or null.</param>
    /// <param name="result"><c>hit</c>, <c>miss</c>, or <c>bypass</c>.</param>
    void RecordOutput(string? endpointKey, string? domain, string result);

    /// <summary>
    /// Records a FusionCache outcome.
    /// </summary>
    /// <param name="endpointKey">e.g. <c>GET /api/x</c>, or null.</param>
    /// <param name="domain">Normalized domain, or null.</param>
    /// <param name="result"><c>hit</c>, <c>miss</c>, <c>stale</c>, <c>bypass</c>, etc.</param>
    /// <param name="elapsedTicks">Optional factory/get duration ticks when latency tracking is on.</param>
    void RecordFusion(string? endpointKey, string? domain, string result, long? elapsedTicks = null);

    /// <summary>Records a successful domain-scoped invalidation.</summary>
    void RecordInvalidation(string domain);

    /// <summary>Snapshot of all counters for the Local Admin API.</summary>
    AdminLiveStatsSnapshot GetSnapshot();
}
