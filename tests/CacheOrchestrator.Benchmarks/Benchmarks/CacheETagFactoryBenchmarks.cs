using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Primitives;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortJob]
public class CacheETagFactoryBenchmarks
{
    private const string Version = "v2026-08-01";
    private const string VersionHex = "a1b2c3d4e5f60708";
    private const string ResourceId = "42";
    private const string PathQuery = "/api/products/42?page=1";

    [Benchmark(Baseline = true)]
    public StringValues FromVersion()
        => CacheETagFactory.FromVersion(Version);

    [Benchmark]
    public StringValues FromVersionAndResource_Id()
        => CacheETagFactory.FromVersionAndResource(VersionHex, ResourceId);

    [Benchmark]
    public StringValues FromVersionAndResource_Path()
        => CacheETagFactory.FromVersionAndResource(VersionHex, PathQuery);
}
