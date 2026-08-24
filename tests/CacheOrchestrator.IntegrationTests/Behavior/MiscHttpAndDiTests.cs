using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;

namespace CacheOrchestrator.IntegrationTests.Behavior;

/// <summary>
/// G — misc integration coverage: metrics smoke, domain attribute wiring, stampede, OC size limits.
/// </summary>
public class MiscHttpAndDiTests
{
    private sealed class FactoryCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private static Dictionary<string, string?> Base(string domain) => new()
    {
        ["Cache:OutputCache:Provider"] = "InMemory",
        ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
        ["Cache:EmitDiagnosticsHeaders"] = "true",
        [$"Cache:Domains:{domain}:Version"] = "v1",
        [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
        [$"Cache:Domains:{domain}:ClientCache:Ttl"] = "00:01:00",
        [$"Cache:Domains:{domain}:ClientCache:TtlMin"] = "00:01:00",
        [$"Cache:Domains:{domain}:OutputCache:Ttl"] = "00:02:00",
        [$"Cache:Domains:{domain}:DataCache:Ttl"] = "00:05:00",
        [$"Cache:Domains:{domain}:FusionCache:Jitter"] = "00:00:00",
    };

    // -------------------------------------------------------------------------
    // G35 — metrics emit when listener subscribed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Metrics_AreEmitted_ForOutputAndFusionPaths()
    {
        string domain = "met-" + Guid.NewGuid().ToString("N");
        long ocCount = 0;
        long fcCount = 0;

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name != CacheOrchestratorMetrics.MeterName)
                return;
            if (instrument.Name is "cache_orchestrator.oc.requests" or "cache_orchestrator.fc.requests")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "cache_orchestrator.oc.requests")
                Interlocked.Add(ref ocCount, measurement);
            if (instrument.Name == "cache_orchestrator.fc.requests")
                Interlocked.Add(ref fcCount, measurement);
        });
        listener.Start();

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(Base(domain))
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config);

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/x", async (HttpContext http, IDomainFusionCache cache) =>
        {
            string v = await cache.GetOrSetAsync(http, _ => Task.FromResult("m"), http.RequestAborted);
            return Results.Text(v);
        }).CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            (await client.GetAsync("/x", TestContext.Current.CancellationToken)).IsSuccessStatusCode.Should().BeTrue();
            (await client.GetAsync("/x", TestContext.Current.CancellationToken)).IsSuccessStatusCode.Should().BeTrue();

            // Allow async metric callbacks
            await Task.Delay(50, TestContext.Current.CancellationToken);

            Volatile.Read(ref ocCount).Should().BeGreaterThan(0, "OC metrics should record miss/hit");
            Volatile.Read(ref fcCount).Should().BeGreaterThan(0, "FC metrics should record at least the first miss");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // G36 — CacheOutputWithDomainAttribute on Minimal API
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CacheOutputWithDomainAttribute_OnMinimalApi_AppliesPolicy()
    {
        string domain = "attr-" + Guid.NewGuid().ToString("N");
        int hits = 0;

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(Base(domain))
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config);

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        app.MapGet("/attr", () =>
            {
                Interlocked.Increment(ref hits);
                return Results.Text("attr-body");
            })
            .WithMetadata(new CacheDomainAttribute(domain))
            .CacheOutputWithDomainAttribute();

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            HttpResponseMessage r1 = await client.GetAsync("/attr", TestContext.Current.CancellationToken);
            r1.IsSuccessStatusCode.Should().BeTrue();
            (await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("attr-body");
            Volatile.Read(ref hits).Should().Be(1);

            HttpResponseMessage r2 = await client.GetAsync("/attr", TestContext.Current.CancellationToken);
            (await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("attr-body");
            Volatile.Read(ref hits).Should().Be(1, "attribute + CacheOutputWithDomainAttribute must enable OC");

            string x2 = r2.Headers.TryGetValues("X-Cache", out IEnumerable<string>? v) ? string.Join(",", v) : "";
            x2.Should().Contain("oc=hit");
            x2.Should().Contain($"domain={domain}");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // G37 — stampede: parallel GetOrSet → single factory
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParallelGetOrSet_Stampede_FactoryRunsOnce()
    {
        string domain = "stampede";
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
                [$"Cache:Domains:{domain}:Version"] = "v1",
                [$"Cache:Domains:{domain}:DataCache:Ttl"] = "00:05:00",
                [$"Cache:Domains:{domain}:FusionCache:Jitter"] = "00:00:00",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IDomainCacheOptionsProvider domains = sp.GetRequiredService<IDomainCacheOptionsProvider>();
        int factoryCalls = 0;

        async Task<string> OneAsync()
        {
            DefaultHttpContext http = new();
            http.Request.Method = "GET";
            http.Request.Path = "/api/stampede";
            domains.EnsureDomainOptions(http, domain);
            return await cache.GetOrSetAsync(http, async ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                await Task.Delay(80, ct);
                return "one";
            }, TestContext.Current.CancellationToken);
        }

        string[] results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => OneAsync()));

        results.Should().OnlyContain(r => r == "one");
        factoryCalls.Should().Be(1, "FusionCache stampede protection must collapse concurrent factories");
    }

    // -------------------------------------------------------------------------
    // G38 — oversized body is not stored (MaximumBodySize)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OutputCache_MaximumBodySize_PreventsStore()
    {
        string domain = "big-" + Guid.NewGuid().ToString("N");
        int hits = 0;

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(Base(domain))
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config, o =>
        {
            o.ConfigureOutputCache(opts =>
            {
                opts.MaximumBodySize = 32; // bytes — force non-store for larger payload
            });
        });

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/x", () =>
        {
            Interlocked.Increment(ref hits);
            return Results.Text(new string('x', 200));
        }).CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            (await client.GetAsync("/x", TestContext.Current.CancellationToken)).IsSuccessStatusCode.Should().BeTrue();
            (await client.GetAsync("/x", TestContext.Current.CancellationToken)).IsSuccessStatusCode.Should().BeTrue();
            Volatile.Read(ref hits).Should().Be(2,
                "responses larger than MaximumBodySize must not be stored in Output Cache");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
