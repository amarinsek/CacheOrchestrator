using CacheOrchestrator.Configuration;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.FusionCache;

/// <summary>
/// Builds <see cref="FusionCacheEntryOptions"/> from domain options + Fusion engine settings.
/// </summary>
internal static class FusionEntryOptionsFactory
{
    /// <summary>
    /// Creates Fusion entry options. Data TTL comes from <paramref name="opts"/>;
    /// hard TTL / fail-safe / jitter / factory timeouts come from <paramref name="fusion"/>.
    /// </summary>
    public static FusionCacheEntryOptions Create(DomainCacheOptions opts, DomainFusionCacheSettings fusion)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(fusion);

        TimeSpan hardTtl = fusion.HardTtl ?? TimeSpan.Zero;
        TimeSpan failSafe = fusion.FailSafe ?? TimeSpan.Zero;
        TimeSpan jitter = fusion.Jitter ?? TimeSpan.Zero;
        TimeSpan factorySoftTimeout = fusion.FactorySoftTimeout ?? TimeSpan.Zero;
        TimeSpan factoryHardTimeout = fusion.FactoryHardTimeout ?? TimeSpan.Zero;
        double eagerRefresh = fusion.EagerRefreshRatio ?? 0;
        int maxItemBytes = fusion.MaxItemBytes ?? 0;
        bool allowBackgroundDistributed = fusion.AllowBackgroundDistributed ?? true;
        bool allowBackgroundBackplane = fusion.AllowBackgroundBackplane ?? true;

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
