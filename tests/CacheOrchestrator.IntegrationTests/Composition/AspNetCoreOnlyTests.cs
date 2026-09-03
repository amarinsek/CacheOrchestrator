using CacheOrchestrator.Configuration;
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
/// Scenario 2: AspNetCore only — Output Cache without Fusion/Hybrid (<see cref="NullDataCacheProvider"/>).
/// </summary>
public class AspNetCoreOnlyTests
{
    private sealed class HitCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private static Dictionary<string, string?> OcDomain(string domain) => new()
    {
        ["Cache:OutputCache:Provider"] = "InMemory",
        ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
        ["Cache:EmitDiagnosticsHeaders"] = "true",
        [$"Cache:Domains:{domain}:Version"] = "v1",
        [$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = "120",
        [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "300",
        [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
        [$"Cache:Domains:{domain}:ClientCache:TtlSeconds"] = "60",
        [$"Cache:Domains:{domain}:ClientCache:TtlMinSeconds"] = "60",
    };

    [Fact]
    public void AspNetCoreOnly_RegistersNullDataCacheProvider()
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
        services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);

        using ServiceProvider sp = services.BuildServiceProvider();
        IDataCacheProvider provider = sp.GetRequiredService<IDataCacheProvider>();
        provider.Name.Should().Be("Null");
        provider.Should().BeSameAs(NullDataCacheProvider.Instance);
    }

    [Fact]
    public async Task AspNetCoreOnly_OutputCache_SecondGet_IsHit_WithoutFusionPackage()
    {
        string domain = "oc-only-" + Guid.NewGuid().ToString("N");
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(OcDomain(domain))
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        builder.Services.AddSingleton<HitCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/x", (HitCounter hits) =>
        {
            hits.Increment();
            return Results.Text("oc-body");
        }).CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            app.Services.GetRequiredService<IDataCacheProvider>().Name.Should().Be("Null");

            HttpResponseMessage r1 = await client.GetAsync("/x", TestContext.Current.CancellationToken);
            string x1 = r1.Headers.TryGetValues("X-CacheOrchestrator", out IEnumerable<string>? v1)
                ? string.Join(",", v1)
                : "";
            (await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("oc-body");
            x1.Should().Contain("oc=miss");

            HttpResponseMessage r2 = await client.GetAsync("/x", TestContext.Current.CancellationToken);
            string x2 = r2.Headers.TryGetValues("X-CacheOrchestrator", out IEnumerable<string>? v2)
                ? string.Join(",", v2)
                : "";
            (await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("oc-body");
            x2.Should().Contain("oc=hit");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AspNetCoreOnly_GetOrSetAsync_AlwaysRunsFactory_ViaNullProvider()
    {
        string domain = "null-dc-" + Guid.NewGuid().ToString("N");
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(OcDomain(domain))
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainDataCache cache = sp.GetRequiredService<IDomainDataCache>();
        IRequestDomainCacheOptions domains = sp.GetRequiredService<IRequestDomainCacheOptions>();

        DefaultHttpContext http = new();
        http.Request.Method = "GET";
        http.Request.Path = "/api/x";
        domains.EnsureDomainOptions(http, domain);

        int calls = 0;
        string a = await cache.GetOrSetAsync(http, _ =>
        {
            calls++;
            return Task.FromResult("a");
        }, TestContext.Current.CancellationToken);
        string b = await cache.GetOrSetAsync(http, _ =>
        {
            calls++;
            return Task.FromResult("b");
        }, TestContext.Current.CancellationToken);

        a.Should().Be("a");
        b.Should().Be("b");
        calls.Should().Be(2, "NullDataCacheProvider must not store values");
    }
}
