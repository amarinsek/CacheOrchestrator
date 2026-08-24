using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

/// <summary>
/// <see cref="FusionEntryOptionsFactory.Create"/> builds Fusion entry options from a domain snapshot.
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
        _ = FusionEntryOptionsFactory.Create(_warm);
    }

    [Benchmark(Baseline = true)]
    public FusionCacheEntryOptions Get_Warm_Reuse()
        => FusionEntryOptionsFactory.Create(_warm);

    [Benchmark]
    public FusionCacheEntryOptions Get_Cold_NewSnapshot()
        => FusionEntryOptionsFactory.Create(CreateOptions());

    private static DomainCacheOptions CreateOptions() => new()
    {
        Domain = "catalog",
        Version = "1",
        VersionHex = "01",
        DataCacheTtl = TimeSpan.FromSeconds(300),
        FusionCacheHardTtl = TimeSpan.FromHours(12),
        FusionCacheFailSafe = TimeSpan.FromHours(24),
        FusionCacheJitterSeconds = 60,
        FusionCacheEagerRefreshRatio = 0.9,
        FusionCacheAllowBackgroundDistributed = true,
        FusionCacheAllowBackgroundBackplane = true,
        OutputCacheEnabled = true,
        DataCacheEnabled = true,
        ClientCacheability = ClientCacheability.Public,
        ClientTtlSeconds = 60,
        ClientTtlMinSeconds = 60,
        OutputTtl = TimeSpan.FromSeconds(60),
        CacheableStatusCodes = [200],
        OutputCacheNamespace = "b",
    };
}
