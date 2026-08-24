using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.Invalidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.IntegrationTests.Fusion;

public class FusionCacheMultiInstanceTests
{
    private ServiceProvider BuildProvider()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:pii:Provider"] = "InMemory",
                ["Cache:Domains:products:DataCache:Instance"] = "default",
                ["Cache:Domains:users:DataCache:Instance"] = "pii",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config);
        services.AddCacheOrchestratorFusionCache(config);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetOrSetAsync_WithMultiInstances_IsolatesDataAndInvalidation()
    {
        await using ServiceProvider provider = BuildProvider();
        IDomainDataCache cache = provider.GetRequiredService<IDomainDataCache>();
        ICacheOrchestratorInvalidator invalidator = provider.GetRequiredService<ICacheOrchestratorInvalidator>();

        DefaultHttpContext productsHttp = new();
        DefaultHttpContext usersHttp = new();

        // 1. Populate both caches
        int productsCalls = 0;
        string productsValue = await cache.GetOrSetAsync(productsHttp, "products", _ =>
        {
            productsCalls++;
            return Task.FromResult("product-1");
        }, TestContext.Current.CancellationToken);

        int usersCalls = 0;
        string usersValue = await cache.GetOrSetAsync(usersHttp, "users", _ =>
        {
            usersCalls++;
            return Task.FromResult("user-1");
        }, TestContext.Current.CancellationToken);

        productsValue.Should().Be("product-1");
        productsCalls.Should().Be(1);

        usersValue.Should().Be("user-1");
        usersCalls.Should().Be(1);

        // 2. Hit again, both should hit
        await cache.GetOrSetAsync(productsHttp, "products", _ =>
        {
            productsCalls++;
            return Task.FromResult("x");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetAsync(usersHttp, "users", _ =>
        {
            usersCalls++;
            return Task.FromResult("y");
        }, TestContext.Current.CancellationToken);

        productsCalls.Should().Be(1); // hit
        usersCalls.Should().Be(1); // hit

        // 3. Invalidate 'products'
        await invalidator.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        // 4. Products should miss, Users should hit
        await cache.GetOrSetAsync(productsHttp, "products", _ =>
        {
            productsCalls++;
            return Task.FromResult("product-2");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetAsync(usersHttp, "users", _ =>
        {
            usersCalls++;
            return Task.FromResult("user-2");
        }, TestContext.Current.CancellationToken);

        productsCalls.Should().Be(2); // miss
        usersCalls.Should().Be(1); // hit

        // 5. Invalidate 'users'
        await invalidator.InvalidateDomainAsync("users", TestContext.Current.CancellationToken);

        // 6. Products should hit, Users should miss
        await cache.GetOrSetAsync(productsHttp, "products", _ =>
        {
            productsCalls++;
            return Task.FromResult("product-3");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetAsync(usersHttp, "users", _ =>
        {
            usersCalls++;
            return Task.FromResult("user-3");
        }, TestContext.Current.CancellationToken);

        productsCalls.Should().Be(2); // hit
        usersCalls.Should().Be(2); // miss
    }
}
