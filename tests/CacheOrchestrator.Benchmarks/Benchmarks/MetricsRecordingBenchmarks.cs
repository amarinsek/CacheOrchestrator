using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Diagnostics;
using System.Diagnostics.Metrics;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

/// <summary>Metrics recording cost with and without an active listener.</summary>
[MemoryDiagnoser]
[ShortJob]
public class MetricsRecordingBenchmarks : IDisposable
{
    private MeterListener? _listener;

    [Params(false, true)]
    public bool ListenerEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (!ListenerEnabled)
            return;

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CacheOrchestratorMetrics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(static (_, _, _, _) => { });
        _listener.SetMeasurementEventCallback<double>(static (_, _, _, _) => { });
        _listener.Start();
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose() => _listener?.Dispose();

    [Benchmark]
    public void RecordDataCacheHit() =>
        CacheOrchestratorMetrics.RecordDataCache("catalog", "hit", 0.8, "GET /api/products/{id}");
}
