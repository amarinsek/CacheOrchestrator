using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class ClientCacheHeaderGeneratorBenchmarks
{
    private DomainCacheOptions _calm = null!;
    private DomainCacheOptions _ramp = null!;
    private DateTimeOffset _now;

    [GlobalSetup]
    public void Setup()
    {
        _now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        _calm = CreateOptions(schedule: _now.AddDays(30));
        _ramp = CreateOptions(schedule: _now.AddMinutes(30));
    }

    [Benchmark(Baseline = true)]
    public string Build_Calm()
        => ClientCacheHeaderGenerator.Build(_calm, _now).Header;

    [Benchmark]
    public string Build_Ramp()
        => ClientCacheHeaderGenerator.Build(_ramp, _now).Header;

    private static DomainCacheOptions CreateOptions(DateTimeOffset schedule) => new()
    {
        Domain = "catalog",
        Version = "1",
        ClientCacheability = ClientCacheability.Public,
        ClientTtlSeconds = 3600,
        ClientTtlMinSeconds = 60,
        ScheduledUpdateUtc = schedule,
        ClientMustRevalidateNearUpdate = false,
        OutputCacheEnabled = true,
        FusionCacheEnabled = true,
        OutputTtl = TimeSpan.FromSeconds(60),
        FusionCacheSoftTtl = TimeSpan.FromSeconds(60),
        CacheableStatusCodes = new[] { 200 },
        OutputCacheNamespace = "b",
        EncodingNormalizationList = null
        // dopolni required polja, ce compiler prosi
    };
}