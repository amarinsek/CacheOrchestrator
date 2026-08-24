using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Orchestration;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Composition;

/// <summary>
/// Scenario 3 (data-cache without OC on the route) and scenario 7 (library <see cref="CacheDomainContext"/>).
/// </summary>
public class DataCacheOnlyAndLibraryTests
{
    private sealed class HitCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    /// <summary>Simulates a class library that only depends on Core.</summary>
    private sealed class CatalogLibrary(ICacheOrchestrator cache)
    {
        public ValueTask<string?> GetProductAsync(
            CacheDomainContext domain,
            string id,
            CancellationToken cancellationToken) =>
            cache.GetOrCreateAsync(
                domain,
                logicalKey: $"product:{id}",
                async ct =>
                {
                    await Task.Yield();
                    return $"lib-{id}";
                },
                cancellationToken);
    }

    [Fact]
    public async Task DataCacheOnly_ExplicitDomain_NoCacheOutputWithDomain_Caches()
    {
        string domain = "dc-only-" + Guid.NewGuid().ToString("N");
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
                ["Cache:EmitDiagnosticsHeaders"] = "true",
                [$"Cache:Domains:{domain}:Version"] = "v1",
                [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "300",
                [$"Cache:Domains:{domain}:FusionCache:JitterSeconds"] = "0",
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        builder.Services.AddCacheOrchestratorFusionCache(config);
        builder.Services.AddSingleton<HitCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        // No .CacheOutputWithDomain — data cache only via explicit domain overload.
        app.MapGet("/x", async (HttpContext http, IDomainDataCache cache, HitCounter hits) =>
        {
            string value = await cache.GetOrSetAsync(http, domain, _ =>
            {
                hits.Increment();
                return Task.FromResult("dc-only");
            }, http.RequestAborted);
            return Results.Text(value);
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            // No CacheOutputWithDomain → no X-Cache from OC policy; assert via factory + body.
            HttpResponseMessage r1 = await client.GetAsync("/x", TestContext.Current.CancellationToken);
            (await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("dc-only");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            HttpResponseMessage r2 = await client.GetAsync("/x", TestContext.Current.CancellationToken);
            (await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("dc-only");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1,
                "explicit domain GetOrSetAsync must cache without Output Cache on the route");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task LibraryCacheDomainContext_HostEndpoint_SharesDomainWithOutputCache()
    {
        string domain = "lib-host-" + Guid.NewGuid().ToString("N");
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
                ["Cache:EmitDiagnosticsHeaders"] = "true",
                [$"Cache:Domains:{domain}:Version"] = "v1",
                [$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = "120",
                [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "300",
                [$"Cache:Domains:{domain}:FusionCache:JitterSeconds"] = "0",
                [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
                [$"Cache:Domains:{domain}:ClientCache:TtlSeconds"] = "60",
                [$"Cache:Domains:{domain}:ClientCache:TtlMinSeconds"] = "60",
            })
            .Build();

        CacheDomainContext libraryDomain = new(domain);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        builder.Services.AddCacheOrchestratorFusionCache(config);
        builder.Services.AddSingleton(libraryDomain);
        builder.Services.AddSingleton<CatalogLibrary>();
        builder.Services.AddSingleton<HitCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/products/{id}", async (
            string id,
            CatalogLibrary lib,
            CacheDomainContext cacheDomain,
            HitCounter hits,
            CancellationToken cancellationToken) =>
        {
            hits.Increment();
            string? dto = await lib.GetProductAsync(cacheDomain, id, cancellationToken);
            return Results.Text(dto ?? "");
        }).CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            HttpResponseMessage r1 = await client.GetAsync("/products/42", TestContext.Current.CancellationToken);
            (await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("lib-42");

            HttpResponseMessage r2 = await client.GetAsync("/products/42", TestContext.Current.CancellationToken);
            string x2 = r2.Headers.TryGetValues("X-Cache", out IEnumerable<string>? values)
                ? string.Join(",", values)
                : "";
            (await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("lib-42");
            // Output Cache should serve the second response without re-entering the endpoint.
            x2.Should().Contain("oc=hit");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
