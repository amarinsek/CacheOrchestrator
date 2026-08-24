using CacheOrchestrator.Configuration;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.Orchestration;
using CacheOrchestrator.OutputCache;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Hybrid;

/// <summary>
/// Scenario 5: AspNetCore + Microsoft HybridCache as <see cref="IDataCacheProvider"/>.
/// </summary>
public class HybridCacheHttpTests
{
    private sealed class FactoryCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private static Dictionary<string, string?> DomainBase(string domain, Action<Dictionary<string, string?>>? extra = null)
    {
        Dictionary<string, string?> d = new()
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientCache:TtlSeconds"] = "60",
            [$"Cache:Domains:{domain}:ClientCache:TtlMinSeconds"] = "60",
            [$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = "120",
            [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "300",
        };
        extra?.Invoke(d);
        return d;
    }

    private static async Task<(HttpClient Client, WebApplication App)> StartHybridHttpAsync(
        Dictionary<string, string?> configValues,
        Action<WebApplication> map,
        Action<IServiceCollection>? configureServices = null)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddHybridCache();
        builder.Services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        builder.Services.AddCacheOrchestratorHybridCache();
        builder.Services.AddSingleton<FactoryCounter>();
        configureServices?.Invoke(builder.Services);

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        map(app);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return (app.GetTestClient(), app);
    }

    private static async Task<(HttpResponseMessage Res, string XCache, string Body)> GetAsync(
        HttpClient client,
        string url)
    {
        HttpResponseMessage res = await client.GetAsync(url, TestContext.Current.CancellationToken);
        string body = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string xCache = res.Headers.TryGetValues("X-Cache", out IEnumerable<string>? values)
            ? string.Join(",", values)
            : string.Empty;
        return (res, xCache, body);
    }

    [Fact]
    public void AddCacheOrchestratorHybridCache_ReplacesFusion_AsDataCacheProvider()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddHybridCache();
        services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        services.AddCacheOrchestratorFusionCache(config);
        services.AddCacheOrchestratorHybridCache();

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<IDataCacheProvider>().Name.Should().Be("HybridCache");
    }

    [Fact]
    public async Task Hybrid_AspNetCore_GetOrSet_SecondCall_IsDataHit()
    {
        string domain = "hy-hit-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = DomainBase(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCache:Enabled"] = "false";
        });

        (HttpClient client, WebApplication app) = await StartHybridHttpAsync(config, a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainDataCache cache, FactoryCounter hits) =>
            {
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    hits.Increment();
                    return Task.FromResult("hybrid-v1");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            r1.IsSuccessStatusCode.Should().BeTrue();
            b1.Should().Be("hybrid-v1");
            x1.Should().Contain("dc=miss");

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            r2.IsSuccessStatusCode.Should().BeTrue();
            b2.Should().Be("hybrid-v1");
            x2.Should().Contain("dc=hit");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Hybrid_InvalidateDomain_ForcesDataMiss()
    {
        string domain = "hy-inv-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = DomainBase(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCache:Enabled"] = "false";
        });

        (HttpClient client, WebApplication app) = await StartHybridHttpAsync(config, a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainDataCache cache, FactoryCounter hits) =>
            {
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    hits.Increment();
                    return Task.FromResult("v" + hits.Count);
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (await GetAsync(client, "/x")).Body.Should().Be("v1");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            await app.Services.GetRequiredService<ICacheOrchestratorInvalidator>()
                .InvalidateDomainAsync(domain, TestContext.Current.CancellationToken);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            b2.Should().Be("v2");
            x2.Should().Contain("dc=miss");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Hybrid_InvalidateEntity_PurgesOnlyTaggedEntry()
    {
        string domain = "hy-ent-" + Guid.NewGuid().ToString("N");
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(DomainBase(domain))
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddHybridCache();
        services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        services.AddCacheOrchestratorHybridCache();
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainDataCache cache = sp.GetRequiredService<IDomainDataCache>();
        IRequestDomainCacheOptions domains = sp.GetRequiredService<IRequestDomainCacheOptions>();
        ICacheOrchestratorInvalidator inv = sp.GetRequiredService<ICacheOrchestratorInvalidator>();

        DefaultHttpContext http1 = new();
        http1.Request.Path = "/api/items/1";
        DefaultHttpContext http2 = new();
        http2.Request.Path = "/api/items/2";
        domains.EnsureDomainOptions(http1, domain);
        domains.EnsureDomainOptions(http2, domain);
        cache.SetEntityIdentity(http1, "items", "1");
        cache.SetEntityIdentity(http2, "items", "2");

        int calls1 = 0;
        int calls2 = 0;

        await cache.GetOrSetEntityAsync(http1, _ =>
        {
            calls1++;
            return Task.FromResult<string?>("p1-v1");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetEntityAsync(http2, _ =>
        {
            calls2++;
            return Task.FromResult<string?>("p2-v1");
        }, TestContext.Current.CancellationToken);

        await inv.InvalidateEntityAsync(domain, "items", "1", TestContext.Current.CancellationToken);

        string? again1 = await cache.GetOrSetEntityAsync(http1, _ =>
        {
            calls1++;
            return Task.FromResult<string?>("p1-v2");
        }, TestContext.Current.CancellationToken);
        string? again2 = await cache.GetOrSetEntityAsync(http2, _ =>
        {
            calls2++;
            return Task.FromResult<string?>("p2-v2");
        }, TestContext.Current.CancellationToken);

        again1.Should().Be("p1-v2");
        again2.Should().Be("p2-v1");
        calls1.Should().Be(2);
        calls2.Should().Be(1);
    }
}
