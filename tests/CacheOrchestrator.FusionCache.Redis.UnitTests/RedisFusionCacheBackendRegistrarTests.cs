using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.FusionCache.Backends;
using CacheOrchestrator.Redis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.FusionCache.Redis.UnitTests;

public class RedisFusionCacheBackendRegistrarTests
{
    private readonly RedisFusionCacheBackendRegistrar _sut = new();

    [Fact]
    public void Name_IsRedis() => _sut.Name.Should().Be("Redis");

    [Fact]
    public void RegisterFusionCache_WhenNoConnectionString_Throws()
    {
        var services = new ServiceCollection();
        IFusionCacheBuilder builder = services.AddFusionCache();
        var options = new CacheOrchestratorOptions();
        var instanceOpts = new CacheOrchestratorOptions.DataCacheInstanceOptions { Provider = "Redis" };
        IConfigurationRoot configuration = new ConfigurationBuilder().Build();
        var context = new FusionCacheRegistrationContext(
            services, configuration, options, "Cache", "default", instanceOpts, "Redis", builder,
            options.GetEffectiveDistributedResilience());

        Action act = () => _sut.RegisterFusionCache(context);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Redis configuration is required*");
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

    [Fact]
    public void RegisterFusionCache_RegistersKeyedDistributedCachePerInstance()
    {
        var services = new ServiceCollection();
        var options = new CacheOrchestratorOptions { Namespace = "app" };
        var defaultOpts = new CacheOrchestratorOptions.DataCacheInstanceOptions { Provider = "Redis" };
        var piiOpts = new CacheOrchestratorOptions.DataCacheInstanceOptions { Provider = "Redis" };
        options.DataCacheInstances["default"] = defaultOpts;
        options.DataCacheInstances["pii"] = piiOpts;

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "localhost:6379",
                ["Cache:DataCacheInstances:pii:Redis:Configuration"] = "other-host:6380"
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
        var instanceOpts = new CacheOrchestratorOptions.DataCacheInstanceOptions { Provider = "Redis" };
        IConfigurationRoot configuration = new ConfigurationBuilder().Build();
        var context = new FusionBackendHealthRegistrationContext(
            services, configuration, "Cache", "pii", "Redis", options, instanceOpts);

        _sut.RegisterHealthProbes(context);
        services.Should().Contain(d => d.ServiceType == typeof(ICacheOrchestratorHealthProbe));
    }

    [Fact]
    public void RegisterFusionCache_WhenContextIsNull_Throws()
    {
        Action act = () => _sut.RegisterFusionCache(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterHealthProbes_WhenContextIsNull_Throws()
    {
        Action act = () => _sut.RegisterHealthProbes(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Project_DoesNotReferenceAspNetCore()
    {
        var refs = typeof(RedisFusionCacheBackendRegistrar).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        refs.Should().NotContain("CacheOrchestrator.AspNetCore");
        refs.Should().NotContain("CacheOrchestrator.AspNetCore.Redis");
    }
}
