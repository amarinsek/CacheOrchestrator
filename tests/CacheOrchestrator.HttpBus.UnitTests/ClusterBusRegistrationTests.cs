using CacheOrchestrator.Cluster;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.HttpBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.HttpBus.UnitTests;

public class ClusterBusRegistrationTests
{
    [Fact]
    public async Task AddHttpClusterBus_WhenEnabled_RegistersHttpBusAndStaticMembership()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Namespace"] = "app1",
            ["Cache:InstanceId"] = "a",
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:Cluster:Bus:Enabled"] = "true",
            ["Cache:Cluster:Bus:Membership"] = "Static",
            ["Cache:Cluster:Bus:Static:Instances:0:Id"] = "a",
            ["Cache:Cluster:Bus:Static:Instances:0:Url"] = "http://127.0.0.1:5001",
            ["Cache:Cluster:Bus:Static:Instances:1:Id"] = "b",
            ["Cache:Cluster:Bus:Static:Instances:1:Url"] = "http://127.0.0.1:5002"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, o => o.AddHttpClusterBus(), enableMvcConvention: false);
        services.AddCacheOrchestratorFusionCache(config);

        await using ServiceProvider sp = services.BuildServiceProvider();
        IClusterCommandBus bus = sp.GetRequiredService<IClusterCommandBus>();
        bus.Should().BeOfType<HttpClusterCommandBus>();
        bus.IsEnabled.Should().BeTrue();

        IClusterMembership membership = sp.GetRequiredService<IClusterMembership>();
        membership.Kind.Should().Be("Static");

        // AspNetCore + Fusion must not double-Bind options (list properties would append).
        IReadOnlyList<ClusterPeer> peers =
            await membership.GetPeersAsync(TestContext.Current.CancellationToken);
        peers.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddHttpClusterBus_WhenServiceDiscovery_RegistersServiceDiscoveryMembership()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:Cluster:Bus:Enabled"] = "true",
            ["Cache:Cluster:Bus:Membership"] = "ServiceDiscovery",
            ["Cache:Cluster:Bus:ServiceDiscovery:ServiceName"] = "app1"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, o => o.AddHttpClusterBus(), enableMvcConvention: false);
        services.AddCacheOrchestratorFusionCache(config);

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
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:Cluster:Bus:Enabled"] = "false",
            ["Cache:Cluster:Bus:Membership"] = "Static"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, o => o.AddHttpClusterBus(), enableMvcConvention: false);
        services.AddCacheOrchestratorFusionCache(config);

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<IClusterCommandBus>().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void AddHttpClusterBus_WhenMembershipNull_RegistersNullMembership()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:Cluster:Bus:Enabled"] = "true",
            ["Cache:Cluster:Bus:Membership"] = "Null"
        }).Build();

        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config, o => o.AddHttpClusterBus(), enableMvcConvention: false);
        services.AddCacheOrchestratorFusionCache(config);

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<IClusterCommandBus>().Should().BeOfType<HttpClusterCommandBus>();
        sp.GetRequiredService<IClusterMembership>().Should().BeSameAs(NullClusterMembership.Instance);
    }

    [Fact]
    public void AddHttpClusterBus_WhenSectionIsWhitespace_Throws()
    {
        ServiceCollection services = new();
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
        }).Build();

        services.AddLogging();
        Func<IServiceCollection> act = () => services.AddCacheOrchestratorAspNetCore(config, o => o.AddHttpClusterBus(" "), enableMvcConvention: false);
        services.AddCacheOrchestratorFusionCache(config);
        act.Should().Throw<ArgumentException>();
    }
}
