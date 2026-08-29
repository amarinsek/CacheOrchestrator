using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Orchestration;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortJob]
public class PhysicalKeyBenchmarks
{
    private readonly DomainCacheOptions _options = new()
    {
        Domain = "catalog:v3",
        VersionHex = "65cd25028f98f158"
    };

    [Benchmark]
    public string NewFormat()
        => CacheOrchestratorService.BuildPhysicalKey(_options, "product:42");
}
