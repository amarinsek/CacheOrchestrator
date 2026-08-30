using CacheOrchestrator.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.FusionCache.UnitTests.DependencyInjection;

public class FusionCacheKeyPrefixRegistrationTests
{
    [Fact]
    public void RegisterNamedInstances_SetsCacheKeyPrefixFromEffectiveNamespace()
    {
        using ServiceProvider sp = BuildProvider(new Dictionary<string, string?>
        {
            ["Cache:Namespace"] = "myapp",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:pii:Provider"] = "InMemory",
        });

        // Force named cache construction (applies builder CacheKeyPrefix into options).
        IFusionCacheProvider provider = sp.GetRequiredService<IFusionCacheProvider>();
        _ = provider.GetCache("default");
        _ = provider.GetCache("pii");

        IOptionsMonitor<FusionCacheOptions> options = sp.GetRequiredService<IOptionsMonitor<FusionCacheOptions>>();
        options.Get("default").CacheKeyPrefix.Should().Be("myapp-dc:");
        options.Get("pii").CacheKeyPrefix.Should().Be("myapp-dc-pii:");
    }

    [Fact]
    public void RegisterNamedInstances_UsesPerInstanceNamespaceOverride()
    {
        using ServiceProvider sp = BuildProvider(new Dictionary<string, string?>
        {
            ["Cache:Namespace"] = "myapp",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Namespace"] = "custom-dc",
        });

        _ = sp.GetRequiredService<IFusionCacheProvider>().GetCache("default");

        sp.GetRequiredService<IOptionsMonitor<FusionCacheOptions>>()
            .Get("default")
            .CacheKeyPrefix.Should().Be("custom-dc:");
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestratorFusionCache(config);
        return services.BuildServiceProvider();
    }
}
