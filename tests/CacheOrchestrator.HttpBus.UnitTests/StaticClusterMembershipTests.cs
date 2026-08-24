using CacheOrchestrator.HttpBus;
using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.HttpBus.UnitTests;

public class StaticClusterMembershipTests
{
    [Fact]
    public void Kind_IsStatic()
    {
        StaticClusterMembership membership = Create(
            new CacheOrchestratorOptions.StaticClusterPeerOptions { Id = "a", Url = "http://127.0.0.1:5001" });
        membership.Kind.Should().Be("Static");
    }

    [Fact]
    public async Task GetPeersAsync_SkipsEmptyIdUrlAndInvalidUri()
    {
        StaticClusterMembership membership = Create(
            new CacheOrchestratorOptions.StaticClusterPeerOptions { Id = "a", Url = "http://127.0.0.1:5001" },
            new CacheOrchestratorOptions.StaticClusterPeerOptions { Id = " ", Url = "http://127.0.0.1:5002" },
            new CacheOrchestratorOptions.StaticClusterPeerOptions { Id = "b", Url = "   " },
            new CacheOrchestratorOptions.StaticClusterPeerOptions { Id = "c", Url = "not-a-uri" },
            new CacheOrchestratorOptions.StaticClusterPeerOptions { Id = "d", Url = "http://10.0.0.2:9" });

        IReadOnlyList<ClusterPeer> peers = await membership.GetPeersAsync(TestContext.Current.CancellationToken);

        peers.Select(p => p.Id).Should().Equal("a", "d");
        peers[0].BaseUrl.Should().Be(new Uri("http://127.0.0.1:5001"));
        peers[1].BaseUrl.Should().Be(new Uri("http://10.0.0.2:9"));
    }

    [Fact]
    public async Task GetPeersAsync_WhenNoInstances_ReturnsEmpty()
    {
        StaticClusterMembership membership = Create();
        IReadOnlyList<ClusterPeer> peers = await membership.GetPeersAsync(TestContext.Current.CancellationToken);
        peers.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WhenOptionsIsNull_Throws()
    {
        var act = () => new StaticClusterMembership(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static StaticClusterMembership Create(
        params CacheOrchestratorOptions.StaticClusterPeerOptions[] instances)
    {
        IOptionsMonitor<CacheOrchestratorOptions> monitor = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        CacheOrchestratorOptions options = new();
        foreach (CacheOrchestratorOptions.StaticClusterPeerOptions instance in instances)
            options.Cluster.Bus.Static.Instances.Add(instance);
        monitor.CurrentValue.Returns(options);
        return new StaticClusterMembership(monitor);
    }
}
