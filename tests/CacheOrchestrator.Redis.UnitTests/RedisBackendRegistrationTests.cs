using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis.UnitTests;

public class RedisBackendRegistrationTests
{
    [Fact]
    public void AddCacheOrchestrator_RedisWithoutConnectionString_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "Redis",
                ["Cache:DataCacheInstances:default:Provider"] = "Redis"
            })
            .Build();

        services.AddLogging();

        Action act = () =>
        {
            services.AddCacheOrchestratorAspNetCore(config, o => o.AddRedisBackend());
            services.AddCacheOrchestratorFusionCache(config);
        };

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Redis*");
    }

    [Fact]
    public void AddRedisBackend_WhenProvidersAreInMemory_RegistersValidatorAndReturnsBuilder()
    {
        var services = new ServiceCollection();
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
            })
            .Build();

        ICacheOrchestratorBuilder? captured = null;
        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, o => captured = o.AddRedisBackend());
        services.AddCacheOrchestratorFusionCache(config);

        captured.Should().NotBeNull();
        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetServices<IValidateOptions<CacheOrchestratorOptions>>()
            .Should().Contain(v => v.GetType().Name == nameof(RedisProviderOptionsValidator));
    }

    [Fact]
    public void AddRedisBackend_WhenSectionIsWhitespace_Throws()
    {
        var services = new ServiceCollection();
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
            })
            .Build();

        services.AddLogging();
        Func<IServiceCollection> act = () => services.AddCacheOrchestratorAspNetCore(config, o => o.AddRedisBackend("  "));
        act.Should().Throw<ArgumentException>();
    }
}
