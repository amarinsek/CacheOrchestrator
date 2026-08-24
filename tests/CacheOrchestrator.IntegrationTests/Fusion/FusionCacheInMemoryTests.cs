using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.IntegrationTests.Fusion;

public class FusionCacheInMemoryTests
{
    private static ServiceProvider BuildProvider()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
                ["Cache:Domains:products:DataCache:Ttl"] = "00:01:00",
                ["Cache:Domains:products:Version"] = "v1"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetOrSetAsync_SecondCall_IsCacheHit()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IRequestDomainCacheOptions domainConfig = sp.GetRequiredService<IRequestDomainCacheOptions>();

        DefaultHttpContext http = new();
        http.Request.Method = "GET";
        http.Request.Path = "/api/products/1";
        domainConfig.EnsureDomainOptions(http, "products");

        int factoryCalls = 0;

        string first = await cache.GetOrSetAsync(http, _ =>
        {
            factoryCalls++;
            return Task.FromResult("product-1");
        }, TestContext.Current.CancellationToken);

        string second = await cache.GetOrSetAsync(http, _ =>
        {
            factoryCalls++;
            return Task.FromResult("should-not-be-called");
        }, TestContext.Current.CancellationToken);

        first.Should().Be("product-1");
        second.Should().Be("product-1");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenFusionDisabled_AlwaysCallsFactory()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCache:Provider"] = "InMemory",
                ["Cache:Domains:products:DataCache:Enabled"] = "false"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IRequestDomainCacheOptions domainConfig = sp.GetRequiredService<IRequestDomainCacheOptions>();

        DefaultHttpContext http = new();
        http.Request.Path = "/api/products/1";
        domainConfig.EnsureDomainOptions(http, "products");

        int factoryCalls = 0;

        await cache.GetOrSetAsync(http, _ =>
        {
            factoryCalls++;
            return Task.FromResult(1);
        }, TestContext.Current.CancellationToken);

        await cache.GetOrSetAsync(http, _ =>
        {
            factoryCalls++;
            return Task.FromResult(2);
        }, TestContext.Current.CancellationToken);

        factoryCalls.Should().Be(2);
    }
}