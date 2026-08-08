using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Mixed;

[Collection("Redis")]
public class MixedBackendTests
{
    private readonly RedisFixture _redis;

    public MixedBackendTests(RedisFixture redis)
    {
        _redis = redis;
    }

    private sealed class HitCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    /// <summary>
    /// Output Cache = InMemory, FusionCache = Redis
    /// </summary>
    [Fact]
    public async Task OutputInMemory_FusionRedis_BothLayersWork()
    {
        string domain = "mixed-oir-" + Guid.NewGuid().ToString("N");
        string basePath = "/" + domain;

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCache:Provider"] = "Redis",
                ["Cache:Redis:Configuration"] = _redis.ConnectionString,
                [$"Cache:Domains:{domain}:OutputCacheTtlSeconds"] = "60",
                [$"Cache:Domains:{domain}:FusionCacheSoftTtlSeconds"] = "60",
                [$"Cache:Domains:{domain}:Version"] = "v1"
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config, o => o.AddRedisBackend());
        builder.Services.AddSingleton<HitCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        app.MapGet(basePath, async (HttpContext http, IDomainFusionCache cache, IDomainCacheOptionsProvider domains, HitCounter hits) =>
        {
            domains.EnsureDomainOptions(http, domain);
            string value = await cache.GetOrSetAsync(http, _ =>
            {
                hits.Increment();
                return Task.FromResult("mixed-value");
            }, http.RequestAborted);
            return Results.Text(value);
        })
        .CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            // 1st request: Output MISS + Fusion MISS ? factory once
            HttpResponseMessage r1 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            r1.IsSuccessStatusCode.Should().BeTrue();
            (await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("mixed-value");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            // 2nd request: Output HIT ? factory not called
            HttpResponseMessage r2 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            r2.IsSuccessStatusCode.Should().BeTrue();
            (await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("mixed-value");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            // Invalidate both layers
            await app.Services.GetRequiredService<ICacheOrchestratorInvalidator>()
                .InvalidateDomainAsync(domain, TestContext.Current.CancellationToken);

            // 3rd request: both MISS ? factory again
            HttpResponseMessage r3 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            r3.IsSuccessStatusCode.Should().BeTrue();
            (await r3.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("mixed-value");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// Output Cache = Redis, FusionCache = InMemory
    /// </summary>
    [Fact]
    public async Task OutputRedis_FusionInMemory_BothLayersWork()
    {
        string domain = "mixed-ori-" + Guid.NewGuid().ToString("N");
        string basePath = "/" + domain;

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "Redis",
                ["Cache:FusionCache:Provider"] = "InMemory",
                ["Cache:Redis:Configuration"] = _redis.ConnectionString,
                [$"Cache:Domains:{domain}:OutputCacheTtlSeconds"] = "60",
                [$"Cache:Domains:{domain}:FusionCacheSoftTtlSeconds"] = "60",
                [$"Cache:Domains:{domain}:Version"] = "v1"
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config, o => o.AddRedisBackend());
        builder.Services.AddSingleton<HitCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        app.MapGet(basePath, async (HttpContext http, IDomainFusionCache cache, IDomainCacheOptionsProvider domains, HitCounter hits) =>
        {
            domains.EnsureDomainOptions(http, domain);
            string value = await cache.GetOrSetAsync(http, _ =>
            {
                hits.Increment();
                return Task.FromResult("mixed-value-2");
            }, http.RequestAborted);
            return Results.Text(value);
        })
        .CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            HttpResponseMessage r1 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            r1.IsSuccessStatusCode.Should().BeTrue();
            (await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("mixed-value-2");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            HttpResponseMessage r2 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            r2.IsSuccessStatusCode.Should().BeTrue();
            (await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("mixed-value-2");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            await app.Services.GetRequiredService<ICacheOrchestratorInvalidator>()
                .InvalidateDomainAsync(domain, TestContext.Current.CancellationToken);

            HttpResponseMessage r3 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            r3.IsSuccessStatusCode.Should().BeTrue();
            (await r3.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("mixed-value-2");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}