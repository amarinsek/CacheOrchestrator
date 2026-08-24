using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.AspNetCore.UnitTests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    // =========================
    // Happy path – InMemory
    // =========================

    [Fact]
    public void AddCacheOrchestrator_WithInMemoryProviders_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
        });

        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config);
        services.AddCacheOrchestratorFusionCache(config);

        using var sp = services.BuildServiceProvider();

        sp.GetService<IDomainCacheOptionsProvider>().Should().NotBeNull();
        sp.GetService<IRequestDomainCacheOptions>().Should().NotBeNull();
        sp.GetService<IDomainDataCache>().Should().NotBeNull();
        sp.GetService<ICacheOrchestrator>().Should().NotBeNull();
        sp.GetService<IDataCacheProvider>().Should().NotBeNull();
        sp.GetService<IDataCacheProvider>()!.Name.Should().Be("FusionCache");
        sp.GetService<IHttpCacheInvalidationSink>().Should().NotBeNull();
        sp.GetService<ICacheOrchestratorInvalidator>().Should().NotBeNull();
        sp.GetService<IDomainKeyGenerator>().Should().NotBeNull();
        sp.GetService<IFusionCacheProvider>().Should().NotBeNull();
        sp.GetService<IOptions<CacheOrchestratorOptions>>().Should().NotBeNull();
    }

    [Fact]
    public void AddCacheOrchestrator_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
        });

        var result = services.AddCacheOrchestratorAspNetCore(config);
        services.AddCacheOrchestratorFusionCache(config);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddCacheOrchestrator_WithCustomSectionName_BindsCorrectly()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MyCache:OutputCache:Provider"] = "InMemory",
            ["MyCache:DataCacheInstances:default:Provider"] = "InMemory",
            ["MyCache:Namespace"] = "custom-ns"
        });

        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, configSection: "MyCache");
        services.AddCacheOrchestratorFusionCache(config);

        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<CacheOrchestratorOptions>>().Value;

        opts.Namespace.Should().Be("custom-ns");
    }

    // =========================
    // Invalid provider
    // =========================

    [Fact]
    public void AddCacheOrchestrator_WithUnsupportedProvider_Throws()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "SqlServer",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
        });

        var act = () => services.AddCacheOrchestratorAspNetCore(config);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Unsupported cache provider*");
    }

    // =========================
    // Redis name without Redis package – unsupported provider
    // =========================

    [Fact]
    public void AddCacheOrchestrator_RedisProviderWithoutAddRedisBackend_ThrowsUnsupportedProvider()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "Redis",
            ["Cache:DataCacheInstances:default:Provider"] = "Redis"
        });

        services.AddLogging();

        var act = () => services.AddCacheOrchestratorAspNetCore(config);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Unsupported cache provider*Redis*");
    }

    // =========================
    // Helper
    // =========================

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
