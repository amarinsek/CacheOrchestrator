using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.Invalidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.IntegrationTests.Fusion;

/// <summary>
/// Verifies that two Redis-backed FusionCache instances can target different Redis endpoints
/// (or databases) without sharing a single global <see cref="IDistributedCache"/>.
/// </summary>
public sealed class FusionCacheMultiInstanceRedisTests : IAsyncLifetime
{
    // Two independent containers (not the shared Redis collection) so each Fusion instance
    // talks to a different Redis endpoint — isolation under test.
    private readonly RedisContainer _redisA = RedisFixture.CreateContainer();
    private readonly RedisContainer _redisB = RedisFixture.CreateContainer();

    public async ValueTask InitializeAsync()
    {
        try
        {
            await Task.WhenAll(_redisA.StartAsync(), _redisB.StartAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to start Redis Testcontainers for multi-instance tests. Docker must be running.",
                ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _redisA.DisposeAsync().ConfigureAwait(false);
        await _redisB.DisposeAsync().ConfigureAwait(false);
    }

    private ServiceProvider BuildProvider()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Namespace"] = "multi",
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "Redis",
                ["Cache:FusionCacheInstances:default:Redis:Configuration"] = _redisA.GetConnectionString(),
                ["Cache:FusionCacheInstances:pii:Provider"] = "Redis",
                ["Cache:FusionCacheInstances:pii:Redis:Configuration"] = _redisB.GetConnectionString(),
                ["Cache:Domains:products:DataCache:Instance"] = "default",
                ["Cache:Domains:products:Version"] = "v1",
                ["Cache:Domains:products:DataCache:Ttl"] = "00:02:00",
                ["Cache:Domains:users:DataCache:Instance"] = "pii",
                ["Cache:Domains:users:Version"] = "v1",
                ["Cache:Domains:users:DataCache:Ttl"] = "00:02:00",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddRedisBackend());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registration_CreatesDistinctKeyedDistributedCachesAndMultiplexers()
    {
        using ServiceProvider sp = BuildProvider();

        IDistributedCache defaultCache = sp.GetRequiredKeyedService<IDistributedCache>("default");
        IDistributedCache piiCache = sp.GetRequiredKeyedService<IDistributedCache>("pii");
        defaultCache.Should().NotBeSameAs(piiCache);

        IConnectionMultiplexer muxDefault = sp.GetRequiredKeyedService<IConnectionMultiplexer>("default");
        IConnectionMultiplexer muxPii = sp.GetRequiredKeyedService<IConnectionMultiplexer>("pii");
        muxDefault.Should().NotBeSameAs(muxPii);

        IFusionCacheProvider fusionProvider = sp.GetRequiredService<IFusionCacheProvider>();
        fusionProvider.GetCache("default").Should().NotBeSameAs(fusionProvider.GetCache("pii"));
    }

    [Fact]
    public async Task GetOrSetAsync_WritesL2OnlyToInstanceRedis_AndInvalidationIsIsolated()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        ICacheOrchestratorInvalidator invalidator = sp.GetRequiredService<ICacheOrchestratorInvalidator>();
        IDomainCacheOptionsProvider domains = sp.GetRequiredService<IDomainCacheOptionsProvider>();

        DefaultHttpContext productsHttp = new();
        productsHttp.Request.Method = "GET";
        productsHttp.Request.Path = "/api/products/1";
        domains.EnsureDomainOptions(productsHttp, "products");

        DefaultHttpContext usersHttp = new();
        usersHttp.Request.Method = "GET";
        usersHttp.Request.Path = "/api/users/1";
        domains.EnsureDomainOptions(usersHttp, "users");

        int productCalls = 0;
        int userCalls = 0;

        await cache.GetOrSetAsync(productsHttp, "products", _ =>
        {
            productCalls++;
            return Task.FromResult("product-v1");
        }, TestContext.Current.CancellationToken);

        await cache.GetOrSetAsync(usersHttp, "users", _ =>
        {
            userCalls++;
            return Task.FromResult("user-v1");
        }, TestContext.Current.CancellationToken);

        productCalls.Should().Be(1);
        userCalls.Should().Be(1);

        // L2 must land on the correct Redis: products → Redis A, users → Redis B.
        await using IConnectionMultiplexer muxA = await ConnectionMultiplexer.ConnectAsync(_redisA.GetConnectionString());
        await using IConnectionMultiplexer muxB = await ConnectionMultiplexer.ConnectAsync(_redisB.GetConnectionString());

        // Allow async L2 write (background distributed ops may apply).
        await WaitUntilAsync(
            async () => await CountKeysAsync(muxA) > 0 && await CountKeysAsync(muxB) > 0,
            timeout: TimeSpan.FromSeconds(10));

        long keysA = await CountKeysAsync(muxA);
        long keysB = await CountKeysAsync(muxB);
        keysA.Should().BeGreaterThan(0, "products L2 should write to Redis A");
        keysB.Should().BeGreaterThan(0, "users L2 should write to Redis B");

        // Hits from L1 (and L2) without factory.
        await cache.GetOrSetAsync(productsHttp, "products", _ =>
        {
            productCalls++;
            return Task.FromResult("x");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetAsync(usersHttp, "users", _ =>
        {
            userCalls++;
            return Task.FromResult("y");
        }, TestContext.Current.CancellationToken);
        productCalls.Should().Be(1);
        userCalls.Should().Be(1);

        // Invalidate only products → users must remain cached.
        await invalidator.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        await cache.GetOrSetAsync(productsHttp, "products", _ =>
        {
            productCalls++;
            return Task.FromResult("product-v2");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetAsync(usersHttp, "users", _ =>
        {
            userCalls++;
            return Task.FromResult("user-v2");
        }, TestContext.Current.CancellationToken);

        productCalls.Should().Be(2);
        userCalls.Should().Be(1);
    }

    private static async Task<long> CountKeysAsync(IConnectionMultiplexer mux)
    {
        IServer server = mux.GetServers().First(s => s.IsConnected);
        long count = 0;
        await foreach (RedisKey _ in server.KeysAsync(pattern: "*"))
            count++;
        return count;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Condition not met within {timeout}.");
    }
}
