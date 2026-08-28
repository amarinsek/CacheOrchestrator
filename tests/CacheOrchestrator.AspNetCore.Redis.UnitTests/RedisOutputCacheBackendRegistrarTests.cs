using CacheOrchestrator.Backends;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.Redis;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.AspNetCore.Redis.UnitTests;

public class RedisOutputCacheBackendRegistrarTests
{
    private readonly RedisOutputCacheBackendRegistrar _sut = new();

    [Fact]
    public void Name_IsRedis() => _sut.Name.Should().Be("Redis");

    [Fact]
    public void RegisterOutputCache_WhenNoConnectionString_Throws()
    {
        var services = new ServiceCollection();
        IConfigurationRoot configuration = new ConfigurationBuilder().Build();
        List<Action<OutputCacheOptions>> configurators = [];
        var context = new OutputCacheRegistrationContext(
            services, configuration, "app-cache-oc", "Cache", "Redis", configurators);

        Action act = () => _sut.RegisterOutputCache(context);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Redis configuration is required*OutputCache*");
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
        services.Should().Contain(d => d.ServiceType == typeof(ICacheOrchestratorHealthProbe));
    }

    [Fact]
    public void RegisterOutputCache_WhenContextIsNull_Throws()
    {
        Action act = () => _sut.RegisterOutputCache(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Project_DoesNotReferenceFusionCacheRedis()
    {
        // Guard: this test assembly must only need AspNetCore.Redis (+ Shared/AspNetCore transitively).
        typeof(RedisOutputCacheBackendRegistrar).Assembly.GetName().Name.Should().Be("CacheOrchestrator.AspNetCore.Redis");
    }
}
