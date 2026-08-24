using CacheOrchestrator.Backends;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.IntegrationTests.Registration;

public class ServiceRegistrationInMemoryTests
{
    [Fact]
    public void AddCacheOrchestrator_InMemory_ResolvesAllCoreServices()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);

        using ServiceProvider sp = services.BuildServiceProvider();

        sp.GetRequiredService<IDomainCacheOptionsProvider>().Should().NotBeNull();
        sp.GetRequiredService<IDomainFusionCache>().Should().NotBeNull();
        sp.GetRequiredService<ICacheOrchestratorInvalidator>().Should().NotBeNull();
        sp.GetRequiredService<IDomainKeyGenerator>().Should().NotBeNull();
        sp.GetRequiredService<IFusionCacheProvider>().Should().NotBeNull();
        sp.GetRequiredService<IOptions<CacheOrchestratorOptions>>().Value.Should().NotBeNull();
    }
}

public class ServiceRegistrationCustomBackendTests
{
    [Fact]
    public void AddCacheOrchestrator_CustomBackend_CallsRegistrarMethods()
    {
        ICacheBackendRegistrar customRegistrar = Substitute.For<ICacheBackendRegistrar>();
        customRegistrar.Name.Returns("CustomDB");
        customRegistrar.SupportsOutputCacheStore.Returns(true);

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "CustomDB",
                ["Cache:DataCacheInstances:default:Provider"] = "CustomDB"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config, builder => builder.AddBackend(customRegistrar));

        using ServiceProvider sp = services.BuildServiceProvider();

        // Verify that the custom registrar was actually called to register things
        customRegistrar.Received(1).RegisterOutputCache(Arg.Any<OutputCacheRegistrationContext>());
        customRegistrar.Received(1).RegisterFusionCache(Arg.Any<FusionCacheRegistrationContext>());
        // default FC instance + oc output health
        customRegistrar.Received().RegisterHealthProbes(Arg.Any<BackendHealthRegistrationContext>());
    }
}

[Collection("Redis")]
public class ServiceRegistrationRedisTests
{
    private readonly RedisFixture _redis;

    public ServiceRegistrationRedisTests(RedisFixture redis)
    {
        _redis = redis;
    }

    [Fact]
    public void AddCacheOrchestrator_Redis_ResolvesAllCoreServices()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "Redis",
                ["Cache:DataCacheInstances:default:Provider"] = "Redis",
                ["Cache:Redis:Configuration"] = _redis.ConnectionString
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddRedisBackend());

        using ServiceProvider sp = services.BuildServiceProvider();

        sp.GetRequiredService<IDomainCacheOptionsProvider>().Should().NotBeNull();
        sp.GetRequiredService<IDomainFusionCache>().Should().NotBeNull();
        sp.GetRequiredService<ICacheOrchestratorInvalidator>().Should().NotBeNull();
        sp.GetRequiredService<IDomainKeyGenerator>().Should().NotBeNull();
        sp.GetRequiredService<IFusionCacheProvider>().Should().NotBeNull();

        CacheOrchestratorOptions opts = sp.GetRequiredService<IOptions<CacheOrchestratorOptions>>().Value;
        opts.OutputCache.Provider.Should().Be("Redis");
        opts.DataCacheInstances["default"].Provider.Should().Be("Redis");
    }

    [Fact]
    public void AddCacheOrchestrator_Redis_CanResolveAndCallInvalidator()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "Redis",
                ["Cache:DataCacheInstances:default:Provider"] = "Redis",
                ["Cache:Redis:Configuration"] = _redis.ConnectionString
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddRedisBackend());

        using ServiceProvider sp = services.BuildServiceProvider();
        ICacheOrchestratorInvalidator invalidator = sp.GetRequiredService<ICacheOrchestratorInvalidator>();

        Func<Task> act = async () => await invalidator.InvalidateDomainAsync(
            "registration-smoke",
            TestContext.Current.CancellationToken);

        act.Should().NotThrowAsync();
    }
}