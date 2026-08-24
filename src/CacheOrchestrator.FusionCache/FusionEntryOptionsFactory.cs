using CacheOrchestrator.Configuration;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.FusionCache;

/// <summary>
/// Builds <see cref="FusionCacheEntryOptions"/> from resolved <see cref="DomainCacheOptions"/>.
/// </summary>
internal static class FusionEntryOptionsFactory
{
    /// <summary>
    /// Creates Fusion entry options from data-cache fields on <paramref name="opts"/>.
    /// Unsupported Hybrid-only consumers ignore these via their own provider.
    /// </summary>
    public static FusionCacheEntryOptions Create(DomainCacheOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);

        TimeSpan hardTtl = opts.DataCacheHardTtl;
        TimeSpan failSafe = opts.DataCacheFailSafe;
        TimeSpan jitter = opts.DataCacheJitter;
        TimeSpan factorySoftTimeout = opts.DataCacheFactorySoftTimeout;
        TimeSpan factoryHardTimeout = opts.DataCacheFactoryHardTimeout;
        double eagerRefresh = opts.DataCacheEagerRefreshRatio;
        int maxItemBytes = opts.DataCacheMaxItemBytes;
        bool allowBackgroundDistributed = opts.DataCacheAllowBackgroundDistributed;
        bool allowBackgroundBackplane = opts.DataCacheAllowBackgroundBackplane;

        // Soft/data duration; cap by hard TTL when hard is shorter (defensive).
        TimeSpan duration = opts.DataCacheTtl;
        if (hardTtl > TimeSpan.Zero && duration > hardTtl)
            duration = hardTtl;

        // Fail-safe must be explicitly enabled; FailSafeMaxDuration alone is ignored by FusionCache.
        bool failSafeEnabled = failSafe > TimeSpan.Zero;

        FusionCacheEntryOptions o = new()
        {
            Duration = duration,
            JitterMaxDuration = jitter < TimeSpan.Zero ? TimeSpan.Zero : jitter,
            IsFailSafeEnabled = failSafeEnabled,
            FailSafeMaxDuration = failSafe,
            AllowBackgroundDistributedCacheOperations = allowBackgroundDistributed,
            AllowBackgroundBackplaneOperations = allowBackgroundBackplane,
        };

        if (eagerRefresh is > 0 and < 1)
            o.EagerRefreshThreshold = (float)eagerRefresh;

        if (maxItemBytes > 0)
            o.Size = maxItemBytes;

        TimeSpan soft = factorySoftTimeout < TimeSpan.Zero ? TimeSpan.Zero : factorySoftTimeout;
        TimeSpan hard = factoryHardTimeout < TimeSpan.Zero ? TimeSpan.Zero : factoryHardTimeout;

        if (hard <= TimeSpan.Zero)
            hard = TimeSpan.FromSeconds(5);

        if (soft <= TimeSpan.Zero || soft >= hard)
            soft = TimeSpan.FromMilliseconds(Math.Max(100, hard.TotalMilliseconds * 0.2));

        o.FactorySoftTimeout = soft;
        o.FactoryHardTimeout = hard;

        return o;
    }
}
