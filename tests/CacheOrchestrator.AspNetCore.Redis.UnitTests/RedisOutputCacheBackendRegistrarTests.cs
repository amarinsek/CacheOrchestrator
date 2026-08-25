using CacheOrchestrator.Backends;
using CacheOrchestrator.Configuration;
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
    public void SupportsOutputCacheStore_IsTrue() => _sut.SupportsOutputCacheStore.Should().BeTrue();

    [Fact]
    public void RegisterOutputCache_WhenNoConnectionString_Throws()
    {
        var services = new ServiceCollection();
        var options = new CacheOrchestratorOptions { OutputCache = { Provider = "Redis" } };
        var configuration = new ConfigurationBuilder().Build();
        List<Action<OutputCacheOptions>> configurators = [];
        var context = new OutputCacheRegistrationContext(
            services, configuration, options, "Cache", "Redis", configurators);

        var act = () => _sut.RegisterOutputCache(context);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Redis configuration is required*OutputCache*");
    }

    [Fact]
    public void RegisterOutputCache_WhenConnectionStringPresent_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var options = new CacheOrchestratorOptions { OutputCache = { Provider = "Redis" } };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "localhost:6379"
            })
            .Build();
        List<Action<OutputCacheOptions>> configurators = [];
        var context = new OutputCacheRegistrationContext(
            services, configuration, options, "Cache", "Redis", configurators);

        var act = () => _sut.RegisterOutputCache(context);
        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterHealthProbes_AddsProbeForInstance()
    {
        var services = new ServiceCollection();
        var options = new CacheOrchestratorOptions();
        var instanceOpts = new CacheOrchestratorOptions.DataCacheInstanceOptions { Provider = "Redis" };
        var configuration = new ConfigurationBuilder().Build();
        var context = new BackendHealthRegistrationContext(
            services, configuration, "Cache", "pii", "Redis", options, instanceOpts);

        _sut.RegisterHealthProbes(context);
        services.Should().Contain(d => d.ServiceType == typeof(ICacheOrchestratorHealthProbe));
    }

    [Fact]
    public void RegisterOutputCache_WhenContextIsNull_Throws()
    {
        var act = () => _sut.RegisterOutputCache(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterHealthProbes_WhenContextIsNull_Throws()
    {
        var act = () => _sut.RegisterHealthProbes(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Project_DoesNotReferenceFusionCacheRedis()
    {
        // Guard: this test assembly must only need AspNetCore.Redis (+ Shared/AspNetCore transitively).
        typeof(RedisOutputCacheBackendRegistrar).Assembly.GetName().Name.Should().Be("CacheOrchestrator.AspNetCore.Redis");
    }
}
