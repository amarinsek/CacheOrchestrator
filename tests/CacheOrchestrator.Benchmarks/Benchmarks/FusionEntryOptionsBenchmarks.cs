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
    private DomainFusionCacheSettings _fusion = null!;

    [GlobalSetup]
    public void Setup()
    {
        _warm = CreateOptions();
        _fusion = CreateFusion();
        _ = FusionEntryOptionsFactory.Create(_warm, _fusion);
    }

    [Benchmark(Baseline = true)]
    public FusionCacheEntryOptions Get_Warm_Reuse()
        => FusionEntryOptionsFactory.Create(_warm, _fusion);

    [Benchmark]
    public FusionCacheEntryOptions Get_Cold_NewSnapshot()
        => FusionEntryOptionsFactory.Create(CreateOptions(), CreateFusion());

    private static DomainCacheOptions CreateOptions() => new()
    {
        Domain = "catalog",
        Version = "1",
        VersionHex = "01",
        DataCacheTtl = TimeSpan.FromSeconds(300),
        OutputCacheEnabled = true,
        DataCacheEnabled = true,
        ClientCacheability = ClientCacheability.Public,
        ClientTtlSeconds = 60,
        ClientTtlMinSeconds = 60,
        OutputTtl = TimeSpan.FromSeconds(60),
        CacheableStatusCodes = [200],
        OutputCacheNamespace = "b",
    };

    private static DomainFusionCacheSettings CreateFusion() => new()
    {
        HardTtlSeconds = 43200,
        FailSafeSeconds = 86400,
        JitterSeconds = 60,
        EagerRefreshRatio = 0.9,
        AllowBackgroundDistributed = true,
        AllowBackgroundBackplane = true,
    };
}
