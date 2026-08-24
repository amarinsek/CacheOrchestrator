using CacheOrchestrator.FusionCache;

namespace CacheOrchestrator.FusionCache.UnitTests.TestSupport;

/// <summary>Test double for <see cref="IFusionDomainSettingsProvider"/>.</summary>
internal sealed class StubFusionDomainSettingsProvider : IFusionDomainSettingsProvider
{
    public DomainFusionCacheSettings Current { get; set; } = CreateDefault();

    public DomainFusionCacheSettings Get(string domain) => Current;

    public static DomainFusionCacheSettings CreateDefault(
        TimeSpan? failSafe = null,
        TimeSpan? hardTtl = null) =>
        new()
        {
            HardTtl = hardTtl ?? TimeSpan.FromHours(1),
            FailSafe = failSafe ?? TimeSpan.FromHours(24),
            Jitter = TimeSpan.FromSeconds(30),
            EagerRefreshRatio = 0.9,
            FactorySoftTimeout = TimeSpan.FromSeconds(1),
            FactoryHardTimeout = TimeSpan.FromSeconds(5),
            AllowBackgroundDistributed = true,
            AllowBackgroundBackplane = true,
        };
}
