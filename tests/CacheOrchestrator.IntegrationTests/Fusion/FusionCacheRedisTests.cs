using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.Invalidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.IntegrationTests.Fusion;

[Collection("Redis")]
public class FusionCacheRedisTests
{
    private readonly RedisFixture _redis;

    public FusionCacheRedisTests(RedisFixture redis)
    {
        _redis = redis;
    }

    private ServiceProvider BuildProvider()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "Redis",
                ["Cache:Redis:Configuration"] = _redis.ConnectionString,
                ["Cache:Domains:products:DataCache:Ttl"] = "00:01:00",
                ["Cache:Domains:products:Version"] = "v1"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddRedisBackend());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetOrSetAsync_SecondCall_IsCacheHit()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IDomainCacheOptionsProvider domainConfig = sp.GetRequiredService<IDomainCacheOptionsProvider>();

        DefaultHttpContext http = new();
        http.Request.Method = "GET";
        http.Request.Path = "/api/products/redis-1";
        domainConfig.EnsureDomainOptions(http, "products");

        int factoryCalls = 0;

        string first = await cache.GetOrSetAsync(http, _ =>
        {
            factoryCalls++;
            return Task.FromResult("redis-product-1");
        }, TestContext.Current.CancellationToken);

        string second = await cache.GetOrSetAsync(http, _ =>
        {
            factoryCalls++;
            return Task.FromResult("should-not-be-called");
        }, TestContext.Current.CancellationToken);

        first.Should().Be("redis-product-1");
        second.Should().Be("redis-product-1");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrSetAsync_AfterInvalidateDomain_IsMissAgain()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        ICacheOrchestratorInvalidator invalidator = sp.GetRequiredService<ICacheOrchestratorInvalidator>();
        IDomainCacheOptionsProvider domainConfig = sp.GetRequiredService<IDomainCacheOptionsProvider>();

        DefaultHttpContext http = new();
        http.Request.Method = "GET";
        http.Request.Path = "/api/products/redis-2";
        domainConfig.EnsureDomainOptions(http, "products");

        int factoryCalls = 0;

        await cache.GetOrSetAsync(http, _ =>
        {
            factoryCalls++;
            return Task.FromResult("before-invalidate");
        }, TestContext.Current.CancellationToken);

        factoryCalls.Should().Be(1);

        await invalidator.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        string after = await cache.GetOrSetAsync(http, _ =>
        {
            factoryCalls++;
            return Task.FromResult("after-invalidate");
        }, TestContext.Current.CancellationToken);

        after.Should().Be("after-invalidate");
        factoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task GetOrSetAsync_DifferentPaths_AreIndependent()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IDomainCacheOptionsProvider domainConfig = sp.GetRequiredService<IDomainCacheOptionsProvider>();

        DefaultHttpContext http1 = new();
        http1.Request.Path = "/api/products/a";
        domainConfig.EnsureDomainOptions(http1, "products");

        DefaultHttpContext http2 = new();
        http2.Request.Path = "/api/products/b";
        domainConfig.EnsureDomainOptions(http2, "products");

        string a = await cache.GetOrSetAsync(http1, _ => Task.FromResult("A"), TestContext.Current.CancellationToken);
        string b = await cache.GetOrSetAsync(http2, _ => Task.FromResult("B"), TestContext.Current.CancellationToken);

        a.Should().Be("A");
        b.Should().Be("B");
    }
}