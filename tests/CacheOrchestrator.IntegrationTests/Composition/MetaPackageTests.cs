using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.Orchestration;
using CacheOrchestrator.OutputCache;
using CacheOrchestrator.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.IntegrationTests.Composition;

/// <summary>
/// Meta package <c>AddCacheOrchestrator</c> = AspNetCore + Fusion (docs scenario 1 / 4).
/// </summary>
public class MetaPackageTests
{
    private sealed class HitCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    [Fact]
    public void Meta_AddCacheOrchestrator_ResolvesFusionAndAspNetCoreServices()
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
        services.AddCacheOrchestrator(config, enableMvcConvention: false);

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<IDomainDataCache>().Should().NotBeNull();
        sp.GetRequiredService<ICacheOrchestrator>().Should().NotBeNull();
        sp.GetRequiredService<IDataCacheProvider>().Name.Should().Be("FusionCache");
        sp.GetRequiredService<IFusionCacheProvider>().Should().NotBeNull();
    }

    [Fact]
    public async Task Meta_AddCacheOrchestrator_HttpGetOrSet_SecondCall_IsDataHit()
    {
        string domain = "meta-" + Guid.NewGuid().ToString("N");
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
                ["Cache:EmitDiagnosticsHeaders"] = "true",
                [$"Cache:Domains:{domain}:Version"] = "v1",
                [$"Cache:Domains:{domain}:OutputCache:Enabled"] = "false",
                [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "300",
                [$"Cache:Domains:{domain}:FusionCache:JitterSeconds"] = "0",
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
        builder.Services.AddCacheOrchestrator(config, enableMvcConvention: false);
        builder.Services.AddSingleton<HitCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/x", async (HttpContext http, IDomainDataCache cache, HitCounter hits) =>
        {
            string value = await cache.GetOrSetAsync(http, _ =>
            {
                hits.Increment();
                return Task.FromResult("meta-v1");
            }, http.RequestAborted);
            return Results.Text(value);
        }).CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            (await client.GetAsync("/x", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await client.GetAsync("/x", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}

[Collection("Redis")]
public class MetaPackageRedisTests
{
    private readonly RedisFixture _redis;

    public MetaPackageRedisTests(RedisFixture redis)
    {
        _redis = redis;
    }

    [Fact]
    public void Meta_AddCacheOrchestrator_WithRedisBackend_Resolves()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "Redis",
                ["Cache:DataCacheInstances:default:Provider"] = "Redis",
                ["Cache:Redis:Configuration"] = _redis.ConnectionString,
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddRedisBackend(), enableMvcConvention: false);

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<IDataCacheProvider>().Name.Should().Be("FusionCache");
        sp.GetRequiredService<IFusionCacheProvider>().Should().NotBeNull();
        sp.GetRequiredService<IDomainDataCache>().Should().NotBeNull();
    }
}
