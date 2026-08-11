using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

/// <summary>
/// <see cref="DomainCacheOptions.GetFusionEntryOptions"/> builds once per domain snapshot then reuses.
/// </summary>
[MemoryDiagnoser]
[ShortJob]
public class FusionEntryOptionsBenchmarks
{
    private DomainCacheOptions _warm = null!;

    [GlobalSetup]
    public void Setup()
    {
        _warm = CreateOptions();
        _ = _warm.GetFusionEntryOptions(); // populate cache
    }

    [Benchmark(Baseline = true)]
    public FusionCacheEntryOptions Get_Warm_Reuse()
        => _warm.GetFusionEntryOptions();

    [Benchmark]
    public FusionCacheEntryOptions Get_Cold_NewSnapshot()
        => CreateOptions().GetFusionEntryOptions();

    private static DomainCacheOptions CreateOptions() => new()
    {
        Domain = "catalog",
        Version = "1",
        VersionHex = "01",
        FusionCacheSoftTtl = TimeSpan.FromSeconds(300),
        FusionCacheHardTtl = TimeSpan.FromHours(12),
        FusionCacheFailSafe = TimeSpan.FromHours(24),
        FusionCacheJitterSeconds = 60,
        FusionCacheEagerRefreshRatio = 0.9,
        FusionCacheAllowBackgroundDistributed = true,
        FusionCacheAllowBackgroundBackplane = true,
        OutputCacheEnabled = true,
        FusionCacheEnabled = true,
        ClientCacheability = ClientCacheability.Public,
        ClientTtlSeconds = 60,
        ClientTtlMinSeconds = 60,
        OutputTtl = TimeSpan.FromSeconds(60),
        CacheableStatusCodes = [200],
        OutputCacheNamespace = "b",
    };
}
