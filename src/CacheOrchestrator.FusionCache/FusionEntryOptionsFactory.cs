using CacheOrchestrator.Configuration;
using System.Runtime.CompilerServices;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.FusionCache;

/// <summary>
/// Builds <see cref="FusionCacheEntryOptions"/> from a domain options snapshot.
/// </summary>
internal static class FusionEntryOptionsFactory
{
    private static readonly ConditionalWeakTable<DomainCacheOptions, FusionCacheEntryOptions> Cache = new();

    /// <summary>
    /// Creates Fusion entry options from <paramref name="opts"/>.
    /// Cached per snapshot instance — safe to reuse across concurrent GetOrSet calls.
    /// </summary>
    public static FusionCacheEntryOptions Create(DomainCacheOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        return Cache.GetValue(opts, static o => Build(o));
    }

    private static FusionCacheEntryOptions Build(DomainCacheOptions opts)
    {
        // Soft/data duration; cap by hard TTL when hard is shorter (defensive).
        TimeSpan duration = opts.DataCacheTtl;
        if (opts.FusionCacheHardTtl > TimeSpan.Zero && duration > opts.FusionCacheHardTtl)
            duration = opts.FusionCacheHardTtl;

        // Fail-safe must be explicitly enabled; FailSafeMaxDuration alone is ignored by FusionCache.
        bool failSafeEnabled = opts.FusionCacheFailSafe > TimeSpan.Zero;

        FusionCacheEntryOptions o = new()
        {
            Duration = duration,
            JitterMaxDuration = TimeSpan.FromSeconds(Math.Max(0, opts.FusionCacheJitterSeconds)),
            IsFailSafeEnabled = failSafeEnabled,
            FailSafeMaxDuration = opts.FusionCacheFailSafe,
            AllowBackgroundDistributedCacheOperations = opts.FusionCacheAllowBackgroundDistributed,
            AllowBackgroundBackplaneOperations = opts.FusionCacheAllowBackgroundBackplane,
        };

        if (opts.FusionCacheEagerRefreshRatio is > 0 and < 1)
            o.EagerRefreshThreshold = (float)opts.FusionCacheEagerRefreshRatio;

        if (opts.FusionCacheMaxItemBytes > 0)
            o.Size = opts.FusionCacheMaxItemBytes;

        TimeSpan soft = TimeSpan.FromSeconds(Math.Max(0, opts.FusionCacheFactorySoftTimeoutSeconds));
        TimeSpan hard = TimeSpan.FromSeconds(Math.Max(0, opts.FusionCacheFactoryHardTimeoutSeconds));

        if (hard <= TimeSpan.Zero)
            hard = TimeSpan.FromSeconds(5);

        if (soft <= TimeSpan.Zero || soft >= hard)
            soft = TimeSpan.FromMilliseconds(Math.Max(100, hard.TotalMilliseconds * 0.2));

        o.FactorySoftTimeout = soft;
        o.FactoryHardTimeout = hard;

        return o;
    }
}
