using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.Invalidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.IntegrationTests.Fusion;

public class FusionCacheEntityInvalidationTests
{
    private static ServiceProvider BuildProvider()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
                ["Cache:Domains:products:Version"] = "v1",
                ["Cache:Domains:products:FusionCacheSoftTtlSeconds"] = "300",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InvalidateEntityAsync_OnlyPurgesThatResource()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        ICacheOrchestratorInvalidator inv = sp.GetRequiredService<ICacheOrchestratorInvalidator>();

        DefaultHttpContext http1 = new();
        http1.Request.Path = "/api/products/1";
        DefaultHttpContext http2 = new();
        http2.Request.Path = "/api/products/2";

        int calls1 = 0;
        int calls2 = 0;

        await cache.GetOrSetAsync(http1, "products", "1", _ =>
        {
            calls1++;
            return Task.FromResult("p1-v1");
        }, TestContext.Current.CancellationToken);

        await cache.GetOrSetAsync(http2, "products", "2", _ =>
        {
            calls2++;
            return Task.FromResult("p2-v1");
        }, TestContext.Current.CancellationToken);

        calls1.Should().Be(1);
        calls2.Should().Be(1);

        // Hits
        await cache.GetOrSetAsync(http1, "products", "1", _ =>
        {
            calls1++;
            return Task.FromResult("x");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetAsync(http2, "products", "2", _ =>
        {
            calls2++;
            return Task.FromResult("y");
        }, TestContext.Current.CancellationToken);
        calls1.Should().Be(1);
        calls2.Should().Be(1);

        // Invalidate only product 1 under same Version
        await inv.InvalidateEntityAsync("products", "1", TestContext.Current.CancellationToken);

        await cache.GetOrSetAsync(http1, "products", "1", _ =>
        {
            calls1++;
            return Task.FromResult("p1-v2");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetAsync(http2, "products", "2", _ =>
        {
            calls2++;
            return Task.FromResult("p2-v2");
        }, TestContext.Current.CancellationToken);

        calls1.Should().Be(2); // miss after entity invalidate
        calls2.Should().Be(1); // still hit
    }
}
