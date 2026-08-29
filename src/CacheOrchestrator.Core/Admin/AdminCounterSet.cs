using System.Diagnostics;

namespace CacheOrchestrator.Admin;

/// <summary>Mutable layer counters (Interlocked updates). Raw only — no derived rates.</summary>
internal sealed class AdminCounterSet
{
    public long OutputCacheHits;
    public long OutputCacheMisses;
    public long OutputCacheBypass;
    public long OutputCacheOff;

    public long DataCacheHits;
    public long DataCacheMisses;
    public long DataCacheStale;
    public long DataCacheBypass;
    public long FactoryRuns;
    public long FactoryFailures;

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
            OutputCacheHits: Interlocked.Read(ref OutputCacheHits),
            OutputCacheMisses: Interlocked.Read(ref OutputCacheMisses),
            OutputCacheBypass: Interlocked.Read(ref OutputCacheBypass),
            OutputCacheOff: Interlocked.Read(ref OutputCacheOff),
            DataCacheHits: Interlocked.Read(ref DataCacheHits),
            DataCacheMisses: Interlocked.Read(ref DataCacheMisses),
            DataCacheStale: Interlocked.Read(ref DataCacheStale),
            DataCacheBypass: Interlocked.Read(ref DataCacheBypass),
            FactoryRuns: Interlocked.Read(ref FactoryRuns),
            FactoryFailures: Interlocked.Read(ref FactoryFailures),
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
    long OutputCacheHits,
    long OutputCacheMisses,
    long OutputCacheBypass,
    long OutputCacheOff,
    long DataCacheHits,
    long DataCacheMisses,
    long DataCacheStale,
    long DataCacheBypass,
    long FactoryRuns,
    long FactoryFailures,
    double? FactoryDurationSumMs,
    long FactoryDurationCount,
    long? FactoryResultSizeSumBytes,
    long FactoryResultSizeCount,
    long Invalidations,
    long LastInvalidationUtcTicks);
