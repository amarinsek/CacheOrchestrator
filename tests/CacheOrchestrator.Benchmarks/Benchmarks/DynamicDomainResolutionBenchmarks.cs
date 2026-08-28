using BenchmarkDotNet.Attributes;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Benchmarks.Benchmarks;

/// <summary>Configured and rejected dynamic-domain resolution through endpoint policy.</summary>
[MemoryDiagnoser]
[ShortJob]
public class DynamicDomainResolutionBenchmarks
{
    private DomainOutputCachePolicy _configuredPolicy = null!;
    private DomainOutputCachePolicy _unknownPolicy = null!;
    private DefaultHttpContext _http = null!;

    [GlobalSetup]
    public void Setup()
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Critical));
        services.AddSingleton<IOptionsMonitor<CacheOrchestratorOptions>>(
            new StaticOptionsMonitor(new CacheOrchestratorOptions
            {
                Domains = { ["tiles-osm"] = new CacheOrchestratorOptions.DomainCacheSettings() }
            }));
        _http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        _configuredPolicy = new DomainOutputCachePolicy(_ => "tiles-osm");
        _unknownPolicy = new DomainOutputCachePolicy(_ => "tiles-unknown");
        _unknownPolicy.ResolveDomain(_http);
    }

    [Benchmark(Baseline = true)]
    public string Configured() => _configuredPolicy.ResolveDomain(_http);

    [Benchmark]
    public string Rejected() => _unknownPolicy.ResolveDomain(_http);

    private sealed class StaticOptionsMonitor(CacheOrchestratorOptions value)
        : IOptionsMonitor<CacheOrchestratorOptions>
    {
        public CacheOrchestratorOptions CurrentValue => value;

        public CacheOrchestratorOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<CacheOrchestratorOptions, string?> listener) => null;
    }
}
