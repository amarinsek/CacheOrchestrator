using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortJob]
public class DomainCacheOptionsProviderBenchmarks : IDisposable
{
    private DomainCacheOptionsProvider _provider = null!;
    private DefaultHttpContext _http = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = new CacheOrchestratorOptions
        {
            Namespace = "bench",
            Domains =
            {
                ["catalog"] = new CacheOrchestratorOptions.DomainCacheSettings
                {
                    Version = "v1",
                    DataCache = new DomainDataCacheSettings { TtlSeconds = 300 },
                }
            }
        };

        var monitor = new FixedOptionsMonitor<CacheOrchestratorOptions>(options);
        _provider = new DomainCacheOptionsProvider(monitor, NullLogger<DomainCacheOptionsProvider>.Instance);

        // Warm L2 snapshot cache
        _ = _provider.GetOrCreateDomainOptions("catalog");

        _http = new DefaultHttpContext();
        _http.Request.Method = "GET";
        _http.Request.Path = "/api/catalog";
    }

    [Benchmark(Baseline = true)]
    public DomainCacheOptions GetOrCreate_L2Hit()
        => _provider.GetOrCreateDomainOptions("catalog");

    [Benchmark]
    public DomainCacheOptions GetOrCreate_Repeated()
        => _provider.GetOrCreateDomainOptions("catalog");

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose() => _provider?.Dispose();

    private sealed class FixedOptionsMonitor<T> : IOptionsMonitor<T>
        where T : class
    {
        public FixedOptionsMonitor(T current)
        {
            CurrentValue = current;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
