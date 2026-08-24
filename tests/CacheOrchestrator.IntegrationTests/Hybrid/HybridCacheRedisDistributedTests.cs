using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.Orchestration;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Hybrid;

/// <summary>
/// HybridCache L2 via <c>IDistributedCache</c> (Redis) — not Fusion <c>AddRedisBackend</c>.
/// </summary>
[Collection("Redis")]
public class HybridCacheRedisDistributedTests
{
    private readonly RedisFixture _redis;

    public HybridCacheRedisDistributedTests(RedisFixture redis)
    {
        _redis = redis;
    }

    private sealed class FactoryCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    [Fact]
    public async Task Hybrid_WithRedisDistributedCache_SecondCall_IsDataHit()
    {
        string domain = "hy-rd-" + Guid.NewGuid().ToString("N");
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
                ["Cache:EmitDiagnosticsHeaders"] = "true",
                [$"Cache:Domains:{domain}:Version"] = "v1",
                [$"Cache:Domains:{domain}:OutputCache:Enabled"] = "false",
                [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "300",
                [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
                [$"Cache:Domains:{domain}:ClientCache:TtlSeconds"] = "60",
                [$"Cache:Domains:{domain}:ClientCache:TtlMinSeconds"] = "60",
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddStackExchangeRedisCache(o => o.Configuration = _redis.ConnectionString);
        builder.Services.AddHybridCache();
        builder.Services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        builder.Services.AddCacheOrchestratorHybridCache();
        builder.Services.AddSingleton<FactoryCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/x", async (HttpContext http, IDomainDataCache cache, FactoryCounter hits) =>
        {
            string value = await cache.GetOrSetAsync(http, _ =>
            {
                hits.Increment();
                return Task.FromResult("redis-l2");
            }, http.RequestAborted);
            return Results.Text(value);
        }).CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            app.Services.GetRequiredService<IDataCacheProvider>().Name.Should().Be("HybridCache");

            HttpResponseMessage r1 = await client.GetAsync("/x", TestContext.Current.CancellationToken);
            string b1 = await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            r1.IsSuccessStatusCode.Should().BeTrue();
            b1.Should().Be("redis-l2");

            HttpResponseMessage r2 = await client.GetAsync("/x", TestContext.Current.CancellationToken);
            string b2 = await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            b2.Should().Be("redis-l2");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
