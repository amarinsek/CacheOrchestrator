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

    public (long Requests, AdminLayerDto Oc, AdminFusionLayerDto Fc, AdminPipelineDto Pipeline) ToStats()
    {
        long ocHits = Interlocked.Read(ref OcHits);
        long ocMisses = Interlocked.Read(ref OcMisses);
        long ocBypass = Interlocked.Read(ref OcBypass);
        long fcHits = Interlocked.Read(ref FcHits);
        long fcMisses = Interlocked.Read(ref FcMisses);
        long fcStale = Interlocked.Read(ref FcStale);
        long fcBypass = Interlocked.Read(ref FcBypass);
        long runs = Interlocked.Read(ref FcFactoryRuns);
        long fails = Interlocked.Read(ref FcFactoryFailures);
        return AdminStatsMath.BuildAll(
            ocHits, ocMisses, ocBypass,
            fcHits, fcMisses, fcStale, fcBypass,
            runs, fails);
    }
}
