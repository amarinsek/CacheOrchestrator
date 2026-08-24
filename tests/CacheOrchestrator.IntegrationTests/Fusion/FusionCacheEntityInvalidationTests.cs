using CacheOrchestrator.Configuration;
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
                ["Cache:Domains:products:DataCache:Ttl"] = "00:05:00",
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
        IRequestDomainCacheOptions domains = sp.GetRequiredService<IRequestDomainCacheOptions>();
        ICacheOrchestratorInvalidator inv = sp.GetRequiredService<ICacheOrchestratorInvalidator>();

        DefaultHttpContext http1 = new();
        http1.Request.Path = "/api/products/1";
        DefaultHttpContext http2 = new();
        http2.Request.Path = "/api/products/2";

        domains.EnsureDomainOptions(http1, "products");
        domains.EnsureDomainOptions(http2, "products");
        cache.SetEntityIdentity(http1, "items", "1");
        cache.SetEntityIdentity(http2, "items", "2");

        int calls1 = 0;
        int calls2 = 0;

        await cache.GetOrSetEntityAsync(http1, _ =>
        {
            calls1++;
            return Task.FromResult<string?>("p1-v1");
        }, TestContext.Current.CancellationToken);

        await cache.GetOrSetEntityAsync(http2, _ =>
        {
            calls2++;
            return Task.FromResult<string?>("p2-v1");
        }, TestContext.Current.CancellationToken);

        calls1.Should().Be(1);
        calls2.Should().Be(1);

        await cache.GetOrSetEntityAsync(http1, _ =>
        {
            calls1++;
            return Task.FromResult<string?>("x");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetEntityAsync(http2, _ =>
        {
            calls2++;
            return Task.FromResult<string?>("y");
        }, TestContext.Current.CancellationToken);
        calls1.Should().Be(1);
        calls2.Should().Be(1);

        await inv.InvalidateEntityAsync("products", "items", "1", TestContext.Current.CancellationToken);

        await cache.GetOrSetEntityAsync(http1, _ =>
        {
            calls1++;
            return Task.FromResult<string?>("p1-v2");
        }, TestContext.Current.CancellationToken);
        await cache.GetOrSetEntityAsync(http2, _ =>
        {
            calls2++;
            return Task.FromResult<string?>("p2-v2");
        }, TestContext.Current.CancellationToken);

        calls1.Should().Be(2);
        calls2.Should().Be(1);
    }

    [Fact]
    public async Task DependsOn_InvalidationPurgesDependentEntry()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IRequestDomainCacheOptions domains = sp.GetRequiredService<IRequestDomainCacheOptions>();
        ICacheOrchestratorInvalidator inv = sp.GetRequiredService<ICacheOrchestratorInvalidator>();

        DefaultHttpContext http = new();
        http.Request.Path = "/api/products/42";
        domains.EnsureDomainOptions(http, "products");
        cache.SetEntityIdentity(http, "items", "42");

        int calls = 0;
        await cache.GetOrSetEntityAsync(http, _ =>
        {
            calls++;
            return Task.FromResult(EntityCache.Create("dto").DependsOn("categories", "7"));
        }, TestContext.Current.CancellationToken);
        calls.Should().Be(1);

        await cache.GetOrSetEntityAsync(http, _ =>
        {
            calls++;
            return Task.FromResult(EntityCache.Create("dto2").DependsOn("categories", "7"));
        }, TestContext.Current.CancellationToken);
        calls.Should().Be(1);

        await inv.InvalidateEntityAsync("products", "categories", "7", TestContext.Current.CancellationToken);

        await cache.GetOrSetEntityAsync(http, _ =>
        {
            calls++;
            return Task.FromResult(EntityCache.Create("dto3").DependsOn("categories", "7"));
        }, TestContext.Current.CancellationToken);
        calls.Should().Be(2);
    }
}
