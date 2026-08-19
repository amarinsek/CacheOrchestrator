using CacheOrchestrator.Backends;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.Redis;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.Redis.UnitTests;

public class RedisCacheBackendRegistrarTests
{
    private readonly RedisCacheBackendRegistrar _sut = new();

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
    public void RegisterFusionCache_WhenNoConnectionString_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddFusionCache();
        var options = new CacheOrchestratorOptions();
        var instanceOpts = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Provider = "Redis" };
        var configuration = new ConfigurationBuilder().Build();
        var context = new FusionCacheRegistrationContext(
            services, configuration, options, "Cache", "default", instanceOpts, "Redis", builder,
            options.GetEffectiveDistributedResilience());

        var act = () => _sut.RegisterFusionCache(context);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Redis configuration is required*");
    }

    [Fact]
    public void RegisterFusionCache_WhenConnectionStringPresent_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var builder = services.AddFusionCache();
        var options = new CacheOrchestratorOptions();
        var instanceOpts = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Provider = "Redis" };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "localhost:6379"
            })
            .Build();
        var context = new FusionCacheRegistrationContext(
            services, configuration, options, "Cache", "default", instanceOpts, "Redis", builder,
            options.GetEffectiveDistributedResilience());

        var act = () => _sut.RegisterFusionCache(context);

        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterFusionCache_RegistersKeyedDistributedCachePerInstance()
    {
        var services = new ServiceCollection();
        var options = new CacheOrchestratorOptions { Namespace = "app" };
        var defaultOpts = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Provider = "Redis" };
        var piiOpts = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Provider = "Redis" };
        options.FusionCacheInstances["default"] = defaultOpts;
        options.FusionCacheInstances["pii"] = piiOpts;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "localhost:6379",
                ["Cache:FusionCacheInstances:pii:Redis:Configuration"] = "other-host:6380"
            })
            .Build();

        _sut.RegisterFusionCache(new FusionCacheRegistrationContext(
            services, configuration, options, "Cache", "default", defaultOpts, "Redis",
            services.AddFusionCache("default"), options.GetEffectiveDistributedResilience()));
        _sut.RegisterFusionCache(new FusionCacheRegistrationContext(
            services, configuration, options, "Cache", "pii", piiOpts, "Redis",
            services.AddFusionCache("pii"), options.GetEffectiveDistributedResilience()));

        var keyedDistributed = services
            .Where(d => d.ServiceType == typeof(IDistributedCache) && d.IsKeyedService)
            .ToList();

        keyedDistributed.Should().HaveCount(2);
        keyedDistributed.Select(d => d.ServiceKey).Should().BeEquivalentTo(["default", "pii"]);
    }

    [Fact]
    public void RegisterHealthProbes_AddsProbeForInstance()
    {
        var services = new ServiceCollection();
        var options = new CacheOrchestratorOptions();
        var instanceOpts = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Provider = "Redis" };
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
    public void RegisterFusionCache_WhenContextIsNull_Throws()
    {
        var act = () => _sut.RegisterFusionCache(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterHealthProbes_WhenContextIsNull_Throws()
    {
        var act = () => _sut.RegisterHealthProbes(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
