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
                    DataCache = new DomainDataCacheSettings { Ttl = TimeSpan.FromSeconds(300) },
                    OutputCache = new DomainOutputCacheSettings { Ttl = TimeSpan.FromSeconds(120) },
                    ClientCache = new DomainClientCacheSettings
                    {
                        Ttl = TimeSpan.FromSeconds(60),
                        TtlMin = TimeSpan.FromSeconds(60),
                        Cacheability = ClientCacheability.Public,
                    },
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
    public DomainCacheOptions Ensure_L1Miss_L2Hit()
    {
        // Fresh HttpContext each time so Items L1 misses; global L2 should hit.
        var http = new DefaultHttpContext();
        return _provider.EnsureDomainOptions(http, "catalog");
    }

    [Benchmark]
    public DomainCacheOptions Ensure_L1Hit()
    {
        // Pin once on shared context, then measure Items hit.
        _ = _provider.EnsureDomainOptions(_http, "catalog");
        return _provider.EnsureDomainOptions(_http, "catalog");
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose() => _provider?.Dispose();

    private sealed class FixedOptionsMonitor<T> : IOptionsMonitor<T>
        where T : class
    {
        public FixedOptionsMonitor(T current) => CurrentValue = current;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
