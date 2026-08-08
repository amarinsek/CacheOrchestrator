using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class XCacheHeaderFormatterBenchmarks
{
    [Benchmark]
    public string Format_Hit()
        => XCacheHeaderFormatter.Format(
            "catalog", ClientCacheClass.Public, OutputCacheResult.Hit, null, null, "v1",
            ClientCacheSchedulePhase.Calm);

    [Benchmark]
    public string Format_MissWithData()
        => XCacheHeaderFormatter.Format(
            "catalog", ClientCacheClass.Public, OutputCacheResult.Miss, DataCacheResult.Miss, 123, "v1",
            ClientCacheSchedulePhase.Approaching);
}