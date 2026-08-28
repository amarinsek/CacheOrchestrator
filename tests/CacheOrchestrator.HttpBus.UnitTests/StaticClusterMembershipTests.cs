using CacheOrchestrator.Cluster;
using CacheOrchestrator.HttpBus;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.HttpBus.UnitTests;

public class StaticClusterMembershipTests
{
    [Fact]
    public void Kind_IsStatic()
    {
        StaticClusterMembership membership = Create(
            new HttpBusStaticPeerOptions { Id = "a", Url = "http://127.0.0.1:5001" });
        membership.Kind.Should().Be("Static");
    }

    [Fact]
    public async Task GetPeersAsync_SkipsEmptyIdUrlAndInvalidUri()
    {
        StaticClusterMembership membership = Create(
            new HttpBusStaticPeerOptions { Id = "a", Url = "http://127.0.0.1:5001" },
            new HttpBusStaticPeerOptions { Id = " ", Url = "http://127.0.0.1:5002" },
            new HttpBusStaticPeerOptions { Id = "b", Url = "   " },
            new HttpBusStaticPeerOptions { Id = "c", Url = "not-a-uri" },
            new HttpBusStaticPeerOptions { Id = "d", Url = "http://10.0.0.2:9" });

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
        Func<StaticClusterMembership> act = () => new StaticClusterMembership(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static StaticClusterMembership Create(
        params HttpBusStaticPeerOptions[] instances)
    {
        HttpBusOptions options = new();
        foreach (HttpBusStaticPeerOptions instance in instances)
            options.Cluster.Bus.Static.Instances.Add(instance);
        IOptionsMonitor<HttpBusOptions> monitor = new FixedOptionsMonitor<HttpBusOptions>(options);
        return new StaticClusterMembership(monitor);
    }
}
