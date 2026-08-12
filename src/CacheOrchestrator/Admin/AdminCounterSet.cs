namespace CacheOrchestrator.Admin;

/// <summary>Mutable layer counters (Interlocked updates).</summary>
internal sealed class AdminCounterSet
{
    public long OcHits;
    public long OcMisses;
    public long OcBypass;

    public long FcHits;
    public long FcMisses;
    public long FcStale;
    public long FcBypass;
    public long FcFactoryRuns;
    public long FcFactoryFailures;

    public long FactorySumTicks;
    public long FactoryCount;

    public long Invalidations;
    public long LastInvalidationUtcTicks;

    public AdminLayerDto ToOcDto()
    {
        long hits = Interlocked.Read(ref OcHits);
        long misses = Interlocked.Read(ref OcMisses);
        long bypass = Interlocked.Read(ref OcBypass);
        return new AdminLayerDto
        {
            Hits = hits,
            Misses = misses,
            Bypass = bypass,
            HitRate = HitRate(hits, misses)
        };
    }

    public AdminFusionLayerDto ToFcDto()
    {
        long hits = Interlocked.Read(ref FcHits);
        long misses = Interlocked.Read(ref FcMisses);
        long stale = Interlocked.Read(ref FcStale);
        long bypass = Interlocked.Read(ref FcBypass);
        return new AdminFusionLayerDto
        {
            Hits = hits,
            Misses = misses,
            Stale = stale,
            Bypass = bypass,
            FactoryRuns = Interlocked.Read(ref FcFactoryRuns),
            FactoryFailures = Interlocked.Read(ref FcFactoryFailures),
            HitRate = HitRate(hits, misses)
        };
    }

    private static double? HitRate(long hits, long misses)
    {
        long total = hits + misses;
        return total <= 0 ? null : (double)hits / total;
    }
}
