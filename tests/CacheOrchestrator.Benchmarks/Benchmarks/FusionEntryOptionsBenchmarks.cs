using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

/// <summary>
/// <see cref="FusionEntryOptionsFactory.Create"/> builds Fusion entry options from domain options.
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
        DataCacheHardTtl = TimeSpan.FromHours(12),
        DataCacheFailSafe = TimeSpan.FromHours(24),
        DataCacheJitter = TimeSpan.FromSeconds(60),
        DataCacheEagerRefreshRatio = 0.9,
        DataCacheAllowBackgroundDistributed = true,
        DataCacheAllowBackgroundBackplane = true,
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
