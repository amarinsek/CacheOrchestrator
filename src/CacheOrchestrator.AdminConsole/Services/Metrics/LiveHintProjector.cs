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
        var (oc, fc, pipe) = BuildLayers(requests, e);
        return new AdminDomainStatsDto
        {
            Name = e.Name,
            Version = config?.Version ?? "",
            VersionIsRuntimeOverride = config?.VersionIsRuntimeOverride ?? false,
            Requests = requests,
            PeakRequestRate = e.RequestRate,
            Oc = oc,
            Fc = fc,
            Pipeline = pipe,
        };
    }

    /// <summary>Projects a live endpoint rate row into Overview-compatible endpoint stats.</summary>
    public static AdminEndpointStatsDto ToEndpointStats(LiveEntityRateDto e)
    {
        long requests = EstimateRequests(e.RequestRate);
        var (oc, fc, pipe) = BuildLayers(requests, e);
        return new AdminEndpointStatsDto
        {
            Route = e.Name,
            ConfiguredDomain = e.Domain,
            Requests = requests,
            PeakRequestRate = e.RequestRate,
            Oc = oc,
            Fc = fc,
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
            Oc = new AdminLayerDto(),
            Fc = new AdminFusionLayerDto(),
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
            OcHitShare = cluster.OcHitShare,
            FcHitShare = cluster.FcHitShare,
            FactoryShare = cluster.FactoryShare,
            FactoryFailShare = cluster.FactoryFailShare,
        };
        return BuildLayers(requests, e).Pipeline;
    }

    public static long EstimateRequests(double requestRate) =>
        Math.Max(0, (long)Math.Round(Math.Max(0, requestRate) * LookbackSeconds));

    private static (AdminLayerDto Oc, AdminFusionLayerDto Fc, AdminPipelineDto Pipeline) BuildLayers(
        long requests,
        LiveEntityRateDto e)
    {
        // Prefer share-based projection so factory/hit rules see the same ratios as Live KPIs.
        double ocHit = Clamp01(e.OcHitShare) ?? 0;
        double fcHit = Clamp01(e.FcHitShare) ?? 0;
        double factory = Clamp01(e.FactoryShare) ?? 0;
        double fail = Clamp01(e.FactoryFailShare) ?? 0;

        long ocHits = (long)Math.Round(requests * ocHit);
        long ocMisses = Math.Max(0, requests - ocHits);
        long fcHits = (long)Math.Round(requests * fcHit);
        long factoryRuns = (long)Math.Round(requests * factory);
        long factoryFailures = (long)Math.Round(requests * fail);
        long fcMisses = Math.Max(factoryRuns, 0);

        (_, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipe) = AdminStatsMath.BuildAll(
            ocHits, ocMisses, ocBypass: 0,
            fcHits, fcMisses, fcStale: 0, fcBypass: 0,
            factoryRuns, factoryFailures);
        return (oc, fc, pipe);
    }

    private static double? Clamp01(double? v) =>
        v is double d && !double.IsNaN(d) && !double.IsInfinity(d)
            ? Math.Clamp(d, 0, 1)
            : null;
}
