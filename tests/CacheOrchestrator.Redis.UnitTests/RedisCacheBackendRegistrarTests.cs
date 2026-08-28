using CacheOrchestrator.Backends;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache.Backends;
using CacheOrchestrator.Redis;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.Redis.UnitTests;

public class RedisCacheBackendRegistrarTests
{
    private readonly RedisCacheBackendRegistrar _sut = new();

    [Fact]
    public void Name_IsRedis() => _sut.Name.Should().Be("Redis");

    [Fact]
    public void MetaRegistrar_ImplementsBothSurfaces()
    {
        _sut.Should().BeAssignableTo<IOutputCacheBackendRegistrar>();
        _sut.Should().BeAssignableTo<IFusionCacheBackendRegistrar>();
    }

    [Fact]
    public void RegisterOutputCache_WhenConnectionStringPresent_DoesNotThrow()
    {
        var services = new ServiceCollection();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "localhost:6379"
            })
            .Build();
        List<Action<OutputCacheOptions>> configurators = [];
        var context = new OutputCacheRegistrationContext(
            services, configuration, "app-cache-oc", "Cache", "Redis", configurators);

        Action act = () => _sut.RegisterOutputCache(context);
        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterFusionCache_WhenConnectionStringPresent_DoesNotThrow()
    {
        var services = new ServiceCollection();
        IFusionCacheBuilder builder = services.AddFusionCache();
        var options = new CacheOrchestratorOptions();
        var instanceOpts = new CacheOrchestratorOptions.DataCacheInstanceOptions { Provider = "Redis" };
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "localhost:6379"
            })
            .Build();
        var context = new FusionCacheRegistrationContext(
            services, configuration, options, "Cache", "default", instanceOpts, "Redis", builder,
            options.GetEffectiveDistributedResilience());

        Action act = () => _sut.RegisterFusionCache(context);
        act.Should().NotThrow();
    }
}
