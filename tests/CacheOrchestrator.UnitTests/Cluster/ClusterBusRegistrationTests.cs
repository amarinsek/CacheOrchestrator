using CacheOrchestrator.Bus;
using CacheOrchestrator.Cluster;
using CacheOrchestrator.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.UnitTests.Cluster;

public class ClusterBusRegistrationTests
{
    [Fact]
    public void AddCacheOrchestrator_WithoutBusPackage_RegistersNullBus()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestrator(config, enableMvcConvention: false);

        using ServiceProvider sp = services.BuildServiceProvider();
        IClusterCommandBus bus = sp.GetRequiredService<IClusterCommandBus>();
        bus.IsEnabled.Should().BeFalse();
        bus.Should().BeSameAs(NullClusterCommandBus.Instance);

        IClusterMembership membership = sp.GetRequiredService<IClusterMembership>();
        membership.Kind.Should().Be("Null");
    }

    [Fact]
    public void AddHttpClusterBus_WhenEnabled_RegistersHttpBusAndStaticMembership()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Namespace"] = "app1",
            ["Cache:InstanceId"] = "a",
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
            ["Cache:Cluster:Bus:Enabled"] = "true",
            ["Cache:Cluster:Bus:Membership"] = "Static",
            ["Cache:Cluster:Bus:Static:Instances:0:Id"] = "a",
            ["Cache:Cluster:Bus:Static:Instances:0:Url"] = "http://127.0.0.1:5001",
            ["Cache:Cluster:Bus:Static:Instances:1:Id"] = "b",
            ["Cache:Cluster:Bus:Static:Instances:1:Url"] = "http://127.0.0.1:5002"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddHttpClusterBus(), enableMvcConvention: false);

        using ServiceProvider sp = services.BuildServiceProvider();
        IClusterCommandBus bus = sp.GetRequiredService<IClusterCommandBus>();
        bus.Should().BeOfType<HttpClusterCommandBus>();
        bus.IsEnabled.Should().BeTrue();

        IClusterMembership membership = sp.GetRequiredService<IClusterMembership>();
        membership.Kind.Should().Be("Static");
    }

    [Fact]
    public async Task AddHttpClusterBus_WhenServiceDiscovery_RegistersServiceDiscoveryMembership()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
            ["Cache:Cluster:Bus:Enabled"] = "true",
            ["Cache:Cluster:Bus:Membership"] = "ServiceDiscovery",
            ["Cache:Cluster:Bus:ServiceDiscovery:ServiceName"] = "app1"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddHttpClusterBus(), enableMvcConvention: false);

        // ServiceEndpointResolver is IAsyncDisposable-only — dispose async.
        await using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<IClusterMembership>().Kind.Should().Be("ServiceDiscovery");
    }

    [Fact]
    public void AddHttpClusterBus_WhenDisabled_IsEnabledFalse()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
            ["Cache:Cluster:Bus:Enabled"] = "false",
            ["Cache:Cluster:Bus:Membership"] = "Static"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddHttpClusterBus(), enableMvcConvention: false);

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<IClusterCommandBus>().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void InstanceIdProvider_UsesCacheInstanceId()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:InstanceId"] = "unit-instance",
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestrator(config, enableMvcConvention: false);

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<IInstanceIdProvider>().InstanceId.Should().Be("unit-instance");
    }
}
