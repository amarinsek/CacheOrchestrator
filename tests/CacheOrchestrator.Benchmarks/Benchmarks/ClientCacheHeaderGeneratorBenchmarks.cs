using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortJob]
public class ClientCacheHeaderGeneratorBenchmarks
{
    private DomainCacheOptions _calm = null!;
    private DomainCacheOptions _ramp = null!;
    private DomainCacheOptions _hold = null!;
    private DomainCacheOptions _mustRevalidate = null!;
    private DomainCacheOptions _noStore = null!;
    private DomainCacheOptions _private = null!;
    private DateTimeOffset _now;

    [GlobalSetup]
    public void Setup()
    {
        _now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        _calm = CreateOptions(schedule: _now.AddDays(30));
        _ramp = CreateOptions(schedule: _now.AddMinutes(30));
        _hold = CreateOptions(schedule: _now.AddMinutes(-5));
        _mustRevalidate = CreateOptions(schedule: _now.AddSeconds(60), mustRevalidateNear: true);
        _noStore = CreateOptions(schedule: null, cacheability: ClientCacheability.NoStore);
        _private = CreateOptions(schedule: null, cacheability: ClientCacheability.Private);
    }

    [Benchmark(Baseline = true)]
    public string Build_Calm()
        => ClientCacheHeaderGenerator.Build(_calm, _now).Header;

    [Benchmark]
    public string Build_Ramp()
        => ClientCacheHeaderGenerator.Build(_ramp, _now).Header;

    [Benchmark]
    public string Build_Hold()
        => ClientCacheHeaderGenerator.Build(_hold, _now).Header;

    [Benchmark]
    public string Build_Approaching_MustRevalidate()
        => ClientCacheHeaderGenerator.Build(_mustRevalidate, _now).Header;

    [Benchmark]
    public string Build_NoStore()
        => ClientCacheHeaderGenerator.Build(_noStore, _now).Header;

    [Benchmark]
    public string Build_Private()
        => ClientCacheHeaderGenerator.Build(_private, _now).Header;

    private static DomainCacheOptions CreateOptions(
        DateTimeOffset? schedule,
        ClientCacheability cacheability = ClientCacheability.Public,
        bool mustRevalidateNear = false) => new()
    {
        Domain = "catalog",
        Version = "1",
        VersionHex = "01",
        ClientCacheability = cacheability,
        ClientTtlSeconds = 3600,
        ClientTtlMinSeconds = 60,
        ScheduledUpdateUtc = schedule,
        ClientMustRevalidateNearUpdate = mustRevalidateNear,
        OutputCacheEnabled = true,
        FusionCacheEnabled = true,
        OutputTtl = TimeSpan.FromSeconds(60),
        FusionCacheSoftTtl = TimeSpan.FromSeconds(60),
        CacheableStatusCodes = [200],
        OutputCacheNamespace = "b",
        EncodingNormalizationList = null,
    };
}
