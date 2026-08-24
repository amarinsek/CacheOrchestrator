using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Services.Hints;

namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>
/// Builds synthetic domain/endpoint stats from Live rates so <see cref="HintEngine"/> can run
/// without nesting <see cref="MetricsWindowStatsService"/> (~18 Prom queries).
/// </summary>
internal static class LiveHintProjector
{
    /// <summary>1m lookback → approximate request count for MinTraffic-style rules.</summary>
    private const double LookbackSeconds = 60;

    public static AdminHintSummaryDto Evaluate(
        HintEngine engine,
        IReadOnlyList<LiveEntityRateDto> domains,
        IReadOnlyList<LiveEntityRateDto> endpoints,
        IReadOnlyList<string> quietDomains,
        IReadOnlyDictionary<string, AdminDomainConfigDto> configByName)
    {
        ArgumentNullException.ThrowIfNull(engine);
        List<AdminHintDto> hints = [];

        foreach (LiveEntityRateDto d in domains)
        {
            configByName.TryGetValue(d.Name, out AdminDomainConfigDto? cfg);
            hints.AddRange(engine.EvaluateDomain(ToDomainStats(d, cfg), cfg));
        }

        foreach (string quiet in quietDomains)
        {
            if (!configByName.TryGetValue(quiet, out AdminDomainConfigDto? cfg))
                continue;
            // Config/schedule rules can still fire with zero live traffic.
            hints.AddRange(engine.EvaluateDomain(ToQuietDomainStats(cfg), cfg));
        }

        foreach (LiveEntityRateDto ep in endpoints)
            hints.AddRange(engine.EvaluateEndpoint(ToEndpointStats(ep)));

        return HintEngine.Summarize(hints);
    }

    /// <summary>Projects a live domain rate row into Overview-compatible domain stats.</summary>
    public static AdminDomainStatsDto ToDomainStats(LiveEntityRateDto e, AdminDomainConfigDto? config = null)
    {
        long requests = EstimateRequests(e.RequestRate);
        var (outputCache, dataCache, pipe) = BuildLayers(requests, e);
        return new AdminDomainStatsDto
        {
            Name = e.Name,
            Version = config?.Version ?? "",
            VersionIsRuntimeOverride = config?.VersionIsRuntimeOverride ?? false,
            Requests = requests,
            PeakRequestRate = e.RequestRate,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe,
        };
    }

    /// <summary>Projects a live endpoint rate row into Overview-compatible endpoint stats.</summary>
    public static AdminEndpointStatsDto ToEndpointStats(LiveEntityRateDto e)
    {
        long requests = EstimateRequests(e.RequestRate);
        var (outputCache, dataCache, pipe) = BuildLayers(requests, e);
        return new AdminEndpointStatsDto
        {
            Route = e.Name,
            ConfiguredDomain = e.Domain,
            Requests = requests,
            PeakRequestRate = e.RequestRate,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe,
        };
    }

    /// <summary>Quiet configured domain (no live traffic) for the Quiet domains table.</summary>
    public static AdminDomainStatsDto ToQuietDomainStats(AdminDomainConfigDto config) =>
        new()
        {
            Name = config.Name,
            Version = config.Version,
            VersionIsRuntimeOverride = config.VersionIsRuntimeOverride,
            Requests = 0,
            PeakRequestRate = 0,
            OutputCache = new AdminLayerDto(),
            DataCache = new AdminDataCacheLayerDto(),
            Pipeline = new AdminPipelineDto(),
        };

    /// <summary>Cluster pipeline panel from live share KPIs.</summary>
    public static AdminPipelineDto ToClusterPipeline(LiveClusterDto cluster)
    {
        long requests = EstimateRequests(cluster.RequestRate ?? 0);
        var e = new LiveEntityRateDto
        {
            Name = "(cluster)",
            RequestRate = cluster.RequestRate ?? 0,
            OutputCacheHitShare = cluster.OutputCacheHitShare,
            DataCacheHitShare = cluster.DataCacheHitShare,
            FactoryShare = cluster.FactoryShare,
            FactoryFailShare = cluster.FactoryFailShare,
        };
        return BuildLayers(requests, e).Pipeline;
    }

    public static long EstimateRequests(double requestRate) =>
        Math.Max(0, (long)Math.Round(Math.Max(0, requestRate) * LookbackSeconds));

    private static (AdminLayerDto OutputCache, AdminDataCacheLayerDto DataCache, AdminPipelineDto Pipeline) BuildLayers(
        long requests,
        LiveEntityRateDto e)
    {
        // Prefer share-based projection so factory/hit rules see the same ratios as Live KPIs.
        double outputCacheHit = Clamp01(e.OutputCacheHitShare) ?? 0;
        double dataCacheHit = Clamp01(e.DataCacheHitShare) ?? 0;
        double factory = Clamp01(e.FactoryShare) ?? 0;
        double fail = Clamp01(e.FactoryFailShare) ?? 0;

        long outputCacheHits = (long)Math.Round(requests * outputCacheHit);
        long outputCacheMisses = Math.Max(0, requests - outputCacheHits);
        long dataCacheHits = (long)Math.Round(requests * dataCacheHit);
        long factoryRuns = (long)Math.Round(requests * factory);
        long factoryFailures = (long)Math.Round(requests * fail);
        long dataCacheMisses = Math.Max(factoryRuns, 0);

        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) = AdminStatsMath.BuildAll(
            outputCacheHits, outputCacheMisses, outputCacheBypass: 0,
            dataCacheHits, dataCacheMisses, dataCacheStale: 0, dataCacheBypass: 0,
            factoryRuns, factoryFailures);
        return (outputCache, dataCache, pipe);
    }

    private static double? Clamp01(double? v) =>
        v is double d && !double.IsNaN(d) && !double.IsInfinity(d)
            ? Math.Clamp(d, 0, 1)
            : null;
}
