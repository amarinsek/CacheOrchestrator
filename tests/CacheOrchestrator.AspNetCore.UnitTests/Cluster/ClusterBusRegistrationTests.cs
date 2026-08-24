using CacheOrchestrator.Cluster;
using CacheOrchestrator.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.AspNetCore.UnitTests.Cluster;

public class ClusterBusRegistrationTests
{
    [Fact]
    public void AddCacheOrchestrator_WithoutBusPackage_RegistersNullBus()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        services.AddCacheOrchestratorFusionCache(config);

        using ServiceProvider sp = services.BuildServiceProvider();
        IClusterCommandBus bus = sp.GetRequiredService<IClusterCommandBus>();
        bus.IsEnabled.Should().BeFalse();
        bus.Should().BeSameAs(NullClusterCommandBus.Instance);

        IClusterMembership membership = sp.GetRequiredService<IClusterMembership>();
        membership.Kind.Should().Be("Null");
    }

    [Fact]
    public void InstanceIdProvider_UsesCacheInstanceId()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:InstanceId"] = "unit-instance",
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, enableMvcConvention: false);
        services.AddCacheOrchestratorFusionCache(config);

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<IInstanceIdProvider>().InstanceId.Should().Be("unit-instance");
    }
}
