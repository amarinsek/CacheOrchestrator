using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortJob]
public class CacheOrchestratorHeaderFormatterBenchmarks
{
    [Benchmark(Baseline = true)]
    public string Format_Hit()
        => CacheOrchestratorHeaderFormatter.Format(
            "catalog", ClientCacheClass.Public, OutputCacheResult.Hit, null, null, "v1",
            ClientCacheSchedulePhase.Calm);

    [Benchmark]
    public string Format_MissWithData()
        => CacheOrchestratorHeaderFormatter.Format(
            "catalog", ClientCacheClass.Public, OutputCacheResult.Miss, DataCacheResult.Miss, 123, "v1",
            ClientCacheSchedulePhase.Approaching);

    [Benchmark]
    public string Format_Stale()
        => CacheOrchestratorHeaderFormatter.Format(
            "catalog", ClientCacheClass.Public, OutputCacheResult.Miss, DataCacheResult.Stale, 45, "v1",
            ClientCacheSchedulePhase.Hold);

    [Benchmark]
    public string Format_Bypass()
        => CacheOrchestratorHeaderFormatter.Format(
            "catalog", ClientCacheClass.NoStore, OutputCacheResult.Bypass, DataCacheResult.Bypass, null, "v1",
            ClientCacheSchedulePhase.NotApplicable);

    [Benchmark]
    public string Format_Blocked()
        => CacheOrchestratorHeaderFormatter.Format(
            "catalog", ClientCacheClass.Blocked, OutputCacheResult.Bypass, null, null, "v1",
            ClientCacheSchedulePhase.NotApplicable);

    [Benchmark]
    public string Format_HoldPhase()
        => CacheOrchestratorHeaderFormatter.Format(
            "catalog", ClientCacheClass.Public, OutputCacheResult.Miss, DataCacheResult.Hit, 12, "v1",
            ClientCacheSchedulePhase.Hold);
}
