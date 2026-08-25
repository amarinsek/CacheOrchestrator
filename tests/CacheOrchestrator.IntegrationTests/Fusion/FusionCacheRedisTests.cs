using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.Invalidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CacheOrchestrator.IntegrationTests.Fusion;

[Collection("Redis")]
public class FusionCacheRedisTests
{
    private readonly RedisFixture _redis;

    public FusionCacheRedisTests(RedisFixture redis)
    {
        _redis = redis;
    }

    private ServiceProvider BuildProvider(string? cacheNamespace = null)
    {
        Dictionary<string, string?> settings = new()
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "Redis",
            ["Cache:Redis:Configuration"] = _redis.ConnectionString,
            ["Cache:Domains:products:DataCache:TtlSeconds"] = "60",
            ["Cache:Domains:products:Version"] = "v1"
        };
        if (!string.IsNullOrWhiteSpace(cacheNamespace))
            settings["Cache:Namespace"] = cacheNamespace;

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, o => o.AddRedisBackend());
        services.AddCacheOrchestratorFusionCache(config);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetOrSetAsync_SecondCall_IsCacheHit()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainDataCache cache = sp.GetRequiredService<IDomainDataCache>();
        IRequestDomainCacheOptions domainConfig = sp.GetRequiredService<IRequestDomainCacheOptions>();

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
        IDomainDataCache cache = sp.GetRequiredService<IDomainDataCache>();
        ICacheOrchestratorInvalidator invalidator = sp.GetRequiredService<ICacheOrchestratorInvalidator>();
        IRequestDomainCacheOptions domainConfig = sp.GetRequiredService<IRequestDomainCacheOptions>();

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
        IDomainDataCache cache = sp.GetRequiredService<IDomainDataCache>();
        IRequestDomainCacheOptions domainConfig = sp.GetRequiredService<IRequestDomainCacheOptions>();

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

    [Fact]
    public async Task GetOrSetAsync_RedisKeys_UseFusionCacheKeyPrefixOnce()
    {
        const string cacheNamespace = "prefix-probe";
        string fcNamespace = cacheNamespace + "-fc";

        await using ServiceProvider sp = BuildProvider(cacheNamespace);
        IDomainDataCache cache = sp.GetRequiredService<IDomainDataCache>();
        IRequestDomainCacheOptions domainConfig = sp.GetRequiredService<IRequestDomainCacheOptions>();

        DefaultHttpContext http = new();
        http.Request.Method = "GET";
        http.Request.Path = "/api/products/prefix-key";
        domainConfig.EnsureDomainOptions(http, "products");

        await cache.GetOrSetAsync(
            http,
            _ => Task.FromResult("prefixed"),
            TestContext.Current.CancellationToken);

        await using IConnectionMultiplexer mux =
            await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        IServer server = mux.GetServers().First(s => s.IsConnected);

        List<string> keys = [];
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            keys.Clear();
            await foreach (RedisKey key in server.KeysAsync(pattern: $"*{fcNamespace}*"))
                keys.Add(key.ToString());

            if (keys.Count > 0)
                break;

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        keys.Should().NotBeEmpty("Fusion L2 should write Redis keys under the FC namespace prefix");
        keys.Should().OnlyContain(k => k.Contains(fcNamespace, StringComparison.Ordinal));
        // Fusion CacheKeyPrefix owns isolation; Redis InstanceName must not apply the same namespace again.
        string doubled = fcNamespace + ":" + fcNamespace;
        keys.Should().NotContain(k => k.Contains(doubled, StringComparison.Ordinal));
        keys.Should().NotContain(k => k.Contains(fcNamespace + fcNamespace, StringComparison.Ordinal));
    }
}