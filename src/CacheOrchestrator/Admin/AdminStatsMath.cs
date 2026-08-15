namespace CacheOrchestrator.Admin;

/// <summary>
/// Shared formulas for Admin live stats: request denominator, layer rates, request shares.
/// </summary>
public static class AdminStatsMath
{
    /// <summary>
    /// Minimum sample size before ratios are considered reliable for UI emphasis.
    /// <list type="bullet">
    /// <item><see cref="AdminLayerDto.LowSample"/> / FC layer: hits+misses on that layer (for rates).</item>
    /// <item><see cref="AdminLayerDto.LowRequestSample"/>: total request denominator (for request shares).</item>
    /// </list>
    /// </summary>
    public const int LowSampleThreshold = 20;

    /// <summary>
    /// Request denominator: prefer Output Cache outcomes (one per OC-managed request);
    /// fall back to Fusion outcomes for Fusion-only traffic.
    /// </summary>
    public static long Requests(
        long ocHits,
        long ocMisses,
        long ocBypass,
        long fcHits,
        long fcMisses,
        long fcStale,
        long fcBypass)
    {
        long oc = ocHits + ocMisses + ocBypass;
        if (oc > 0)
            return oc;

        return fcHits + fcMisses + fcStale + fcBypass;
    }

    /// <summary>Layer hit rate: hits / (hits + misses); null when no layer traffic.</summary>
    public static double? LayerHitRate(long hits, long misses)
    {
        long n = hits + misses;
        return n <= 0 ? null : (double)hits / n;
    }

    /// <summary>Layer miss rate: misses / (hits + misses).</summary>
    public static double? LayerMissRate(long hits, long misses)
    {
        long n = hits + misses;
        return n <= 0 ? null : (double)misses / n;
    }

    /// <summary>Share of total requests: count / requests; null when requests is 0.</summary>
    public static double? Share(long count, long requests) =>
        requests <= 0 ? null : (double)count / requests;

    public static AdminLayerDto BuildOc(
        long hits,
        long misses,
        long bypass,
        long requests)
    {
        long layerSample = hits + misses;
        return new AdminLayerDto
        {
            Hits = hits,
            Misses = misses,
            Bypass = bypass,
            LayerSampleSize = layerSample,
            HitRate = LayerHitRate(hits, misses),
            MissRate = LayerMissRate(hits, misses),
            HitShare = Share(hits, requests),
            MissShare = Share(misses, requests),
            BypassShare = Share(bypass, requests),
            // Rates need enough layer events; shares need enough total requests.
            LowSample = layerSample is > 0 and < LowSampleThreshold,
            LowRequestSample = requests is > 0 and < LowSampleThreshold
        };
    }

    public static AdminFusionLayerDto BuildFc(
        long hits,
        long misses,
        long stale,
        long bypass,
        long factoryRuns,
        long factoryFailures,
        long requests)
    {
        long layerSample = hits + misses;
        long layerWithStale = hits + misses + stale;
        return new AdminFusionLayerDto
        {
            Hits = hits,
            Misses = misses,
            Stale = stale,
            Bypass = bypass,
            FactoryRuns = factoryRuns,
            FactoryFailures = factoryFailures,
            LayerSampleSize = layerSample,
            HitRate = LayerHitRate(hits, misses),
            MissRate = LayerMissRate(hits, misses),
            StaleRate = layerWithStale <= 0 ? null : (double)stale / layerWithStale,
            HitShare = Share(hits, requests),
            MissShare = Share(misses, requests),
            StaleShare = Share(stale, requests),
            BypassShare = Share(bypass, requests),
            FactoryShare = Share(factoryRuns, requests),
            // Layer rates: few FC hit/miss events → unreliable rate (even if many OC hits).
            LowSample = layerSample is > 0 and < LowSampleThreshold,
            // Request shares: reliability follows total request denominator, not layer n.
            LowRequestSample = requests is > 0 and < LowSampleThreshold
        };
    }

    /// <summary>Builds OC+FC DTOs and pipeline shares from raw counters.</summary>
    public static (
        long Requests,
        AdminLayerDto Oc,
        AdminFusionLayerDto Fc,
        AdminPipelineDto Pipeline) BuildAll(
        long ocHits,
        long ocMisses,
        long ocBypass,
        long fcHits,
        long fcMisses,
        long fcStale,
        long fcBypass,
        long factoryRuns,
        long factoryFailures)
    {
        long requests = Requests(ocHits, ocMisses, ocBypass, fcHits, fcMisses, fcStale, fcBypass);
        AdminLayerDto oc = BuildOc(ocHits, ocMisses, ocBypass, requests);
        AdminFusionLayerDto fc = BuildFc(
            fcHits, fcMisses, fcStale, fcBypass, factoryRuns, factoryFailures, requests);

        // Pipeline: OC hit | FC hit | factory | bypass | other
        // OC miss typically continues to FC — do not put OC miss in the bar separately.
        AdminPipelineDto pipeline = new()
        {
            OcHitShare = oc.HitShare,
            FcHitShare = fc.HitShare,
            FactoryShare = fc.FactoryShare,
            BypassShare = Share(ocBypass + fcBypass, requests),
            OtherShare = requests <= 0
                ? null
                : Share(
                    Math.Max(0, requests - ocHits - fcHits - factoryRuns - ocBypass - fcBypass),
                    requests)
        };

        return (requests, oc, fc, pipeline);
    }

    /// <summary>min / max / mean / stdev over a set of optional ratios (instance breakdown).</summary>
    public static AdminShareSpreadDto? Spread(IEnumerable<double?> values)
    {
        double[] xs = [.. values.Where(v => v is double).Select(v => v!.Value)];
        if (xs.Length == 0)
            return null;

        double min = xs.Min();
        double max = xs.Max();
        double mean = xs.Average();
        double? stdev = null;
        if (xs.Length >= 2)
        {
            double var = xs.Sum(x => (x - mean) * (x - mean)) / xs.Length;
            stdev = Math.Sqrt(var);
        }

        return new AdminShareSpreadDto
        {
            Min = min,
            Max = max,
            Mean = mean,
            Stdev = stdev,
            SampleCount = xs.Length
        };
    }
}
