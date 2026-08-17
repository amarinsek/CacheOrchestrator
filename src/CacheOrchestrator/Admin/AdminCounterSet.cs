using System.Diagnostics;

namespace CacheOrchestrator.Admin;

/// <summary>Mutable layer counters (Interlocked updates). Raw only — no derived rates.</summary>
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

    /// <summary>Sum of factory-path Stopwatch ticks (miss/stale), when TrackLatency is on.</summary>
    public long FactorySumTicks;

    /// <summary>Factory-path duration samples.</summary>
    public long FactoryCount;

    /// <summary>Sum of measured factory result sizes (bytes), when TrackResultSize is on.</summary>
    public long FactoryResultSizeSumBytes;

    /// <summary>Factory result size samples.</summary>
    public long FactoryResultSizeCount;

    public long Invalidations;
    public long LastInvalidationUtcTicks;

    /// <summary>Atomic read of all counters into a snapshot value.</summary>
    public AdminCounterSnapshot Read()
    {
        long sumTicks = Interlocked.Read(ref FactorySumTicks);
        long count = Interlocked.Read(ref FactoryCount);
        double? sumMs = count > 0
            ? sumTicks * 1000.0 / Stopwatch.Frequency
            : null;

        long sizeSum = Interlocked.Read(ref FactoryResultSizeSumBytes);
        long sizeCount = Interlocked.Read(ref FactoryResultSizeCount);

        return new AdminCounterSnapshot(
            OcHits: Interlocked.Read(ref OcHits),
            OcMisses: Interlocked.Read(ref OcMisses),
            OcBypass: Interlocked.Read(ref OcBypass),
            FcHits: Interlocked.Read(ref FcHits),
            FcMisses: Interlocked.Read(ref FcMisses),
            FcStale: Interlocked.Read(ref FcStale),
            FcBypass: Interlocked.Read(ref FcBypass),
            FactoryRuns: Interlocked.Read(ref FcFactoryRuns),
            FactoryFailures: Interlocked.Read(ref FcFactoryFailures),
            FactoryDurationSumMs: sumMs,
            FactoryDurationCount: count,
            FactoryResultSizeSumBytes: sizeCount > 0 ? sizeSum : null,
            FactoryResultSizeCount: sizeCount,
            Invalidations: Interlocked.Read(ref Invalidations),
            LastInvalidationUtcTicks: Interlocked.Read(ref LastInvalidationUtcTicks));
    }
}

/// <summary>Immutable counter snapshot for mapping to raw or v1 DTOs.</summary>
internal readonly record struct AdminCounterSnapshot(
    long OcHits,
    long OcMisses,
    long OcBypass,
    long FcHits,
    long FcMisses,
    long FcStale,
    long FcBypass,
    long FactoryRuns,
    long FactoryFailures,
    double? FactoryDurationSumMs,
    long FactoryDurationCount,
    long? FactoryResultSizeSumBytes,
    long FactoryResultSizeCount,
    long Invalidations,
    long LastInvalidationUtcTicks);
