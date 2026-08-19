using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.UnitTests.DependencyInjection;

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
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory"
        });

        services.AddLogging();
        services.AddCacheOrchestrator(config);

        using var sp = services.BuildServiceProvider();

        sp.GetService<IDomainCacheOptionsProvider>().Should().NotBeNull();
        sp.GetService<IDomainFusionCache>().Should().NotBeNull();
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
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory"
        });

        var result = services.AddCacheOrchestrator(config);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddCacheOrchestrator_WithCustomSectionName_BindsCorrectly()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MyCache:OutputCache:Provider"] = "InMemory",
            ["MyCache:FusionCacheInstances:default:Provider"] = "InMemory",
            ["MyCache:Namespace"] = "custom-ns"
        });

        services.AddLogging();
        services.AddCacheOrchestrator(config, configSection: "MyCache");

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
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory"
        });

        var act = () => services.AddCacheOrchestrator(config);

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
            ["Cache:FusionCacheInstances:default:Provider"] = "Redis"
        });

        services.AddLogging();

        var act = () => services.AddCacheOrchestrator(config);

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