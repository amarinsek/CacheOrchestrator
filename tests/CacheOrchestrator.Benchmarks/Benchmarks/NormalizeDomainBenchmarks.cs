using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortJob]
public class NormalizeDomainBenchmarks
{
    private const string Clean = "catalog";
    private const string AlreadyNormalized = "product-detail";
    private const string Dirty = "  Catalog--Name!!  ";
    private const string WithAllowedExtras = "tenant@org:maps_osm";
    private const string EmptyLike = "   ---   ";
    private const string ResourceId = "Product-42";
    private const string ResourceIdDirty = "  ID::99!!  ";

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

    [Benchmark]
    public string NormalizeResourceId_Clean()
        => DomainName.NormalizeResourceId(ResourceId);

    [Benchmark]
    public string NormalizeResourceId_Dirty()
        => DomainName.NormalizeResourceId(ResourceIdDirty);
}
