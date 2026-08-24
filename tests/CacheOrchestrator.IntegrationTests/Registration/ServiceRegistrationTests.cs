using CacheOrchestrator.Backends;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using CacheOrchestrator.DataCache;
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
        services.AddCacheOrchestratorAspNetCore(config);
        services.AddCacheOrchestratorFusionCache(config);

        using ServiceProvider sp = services.BuildServiceProvider();

        sp.GetRequiredService<IDomainCacheOptionsProvider>().Should().NotBeNull();
        sp.GetRequiredService<IDomainDataCache>().Should().NotBeNull();
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
        ICacheBackendRegistrar customOc = Substitute.For<ICacheBackendRegistrar>();
        customOc.Name.Returns("CustomDB");
        customOc.SupportsOutputCacheStore.Returns(true);

        CacheOrchestrator.FusionCache.Backends.IFusionCacheBackendRegistrar customFusion =
            Substitute.For<CacheOrchestrator.FusionCache.Backends.IFusionCacheBackendRegistrar>();
        customFusion.Name.Returns("CustomDB");

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "CustomDB",
                ["Cache:DataCacheInstances:default:Provider"] = "CustomDB"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, builder => builder.AddBackend(customOc));
        services.AddFusionCacheBackend(customFusion);
        services.AddCacheOrchestratorFusionCache(config);

        using ServiceProvider sp = services.BuildServiceProvider();

        customOc.Received(1).RegisterOutputCache(Arg.Any<OutputCacheRegistrationContext>());
        customOc.Received().RegisterHealthProbes(Arg.Any<BackendHealthRegistrationContext>());
        customFusion.Received(1).RegisterFusionCache(Arg.Any<CacheOrchestrator.FusionCache.Backends.FusionCacheRegistrationContext>());
        customFusion.Received().RegisterHealthProbes(Arg.Any<CacheOrchestrator.FusionCache.Backends.FusionBackendHealthRegistrationContext>());
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
        services.AddCacheOrchestratorAspNetCore(config, o => o.AddRedisBackend());
        services.AddCacheOrchestratorFusionCache(config);

        using ServiceProvider sp = services.BuildServiceProvider();

        sp.GetRequiredService<IDomainCacheOptionsProvider>().Should().NotBeNull();
        sp.GetRequiredService<IDomainDataCache>().Should().NotBeNull();
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
        services.AddCacheOrchestratorAspNetCore(config, o => o.AddRedisBackend());
        services.AddCacheOrchestratorFusionCache(config);

        using ServiceProvider sp = services.BuildServiceProvider();
        ICacheOrchestratorInvalidator invalidator = sp.GetRequiredService<ICacheOrchestratorInvalidator>();

        Func<Task> act = async () => await invalidator.InvalidateDomainAsync(
            "registration-smoke",
            TestContext.Current.CancellationToken);

        act.Should().NotThrowAsync();
    }
}
