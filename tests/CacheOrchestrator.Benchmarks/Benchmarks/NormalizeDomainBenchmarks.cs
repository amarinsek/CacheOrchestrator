using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3, launchCount: 1)]
public class NormalizeDomainBenchmarks
{
    private const string Clean = "catalog";
    private const string AlreadyNormalized = "product-detail";
    private const string Dirty = "  Catalog--Name!!  ";
    private const string WithAllowedExtras = "tenant@org:maps_osm";
    private const string EmptyLike = "   ---   ";

    [Benchmark(Baseline = true)]
    public string Clean_Input()
        => DomainName.Normalize(Clean);

    [Benchmark]
    public string Already_Normalized()
        => DomainName.Normalize(AlreadyNormalized);

    [Benchmark]
    public string Dirty_Input()
        => DomainName.Normalize(Dirty);

    [Benchmark]
    public string Allowed_SpecialChars()
        => DomainName.Normalize(WithAllowedExtras);

    [Benchmark]
    public string Empty_After_Normalize()
        => DomainName.Normalize(EmptyLike);
}