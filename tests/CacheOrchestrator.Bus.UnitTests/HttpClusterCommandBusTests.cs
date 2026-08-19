using CacheOrchestrator.Bus;
using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;

namespace CacheOrchestrator.Bus.UnitTests;

public class HttpClusterCommandBusTests
{
    [Fact]
    public void BuildApplyUri_CombinesBaseAndPrefix()
    {
        Uri uri = HttpClusterCommandBus.BuildApplyUri(
            new Uri("http://10.0.0.1:8080/"),
            "/cache-admin/local");

        uri.ToString().Should().Be("http://10.0.0.1:8080/cache-admin/local/cluster/apply");
    }

    [Fact]
    public async Task PublishAsync_SkipsSelfAndPostsToPeers()
    {
        List<HttpRequestMessage> seen = [];
        RecordingHandler handler = new(seen, HttpStatusCode.OK);

        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(HttpClusterCommandBus.HttpClientName)
            .Returns(new HttpClient(handler) { BaseAddress = null });

        IClusterMembership membership = Substitute.For<IClusterMembership>();
        membership.Kind.Returns("Static");
        membership.GetPeersAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ClusterPeer("a", new Uri("http://127.0.0.1:5001")),
            new ClusterPeer("b", new Uri("http://127.0.0.1:5002"))
        ]);

        IInstanceIdProvider instanceId = Substitute.For<IInstanceIdProvider>();
        instanceId.InstanceId.Returns("a");

        IOptionsMonitor<CacheOrchestratorOptions> options = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            Namespace = "app1",
            Admin = { RoutePrefix = "/cache-admin/local", ApiKey = "k" },
            Cluster =
            {
                Bus =
                {
                    Enabled = true,
                    PeerTimeoutMs = 2000,
                    MaxParallelism = 8,
                    Membership = "Static"
                }
            }
        });

        HttpClusterCommandBus bus = new(
            factory,
            membership,
            instanceId,
            options,
            NullLogger<HttpClusterCommandBus>.Instance);

        InvalidateCommand command = new()
        {
            CommandId = Guid.NewGuid(),
            OriginInstanceId = "a",
            Namespace = "app1",
            TimestampUtc = DateTimeOffset.UtcNow,
            Kind = CacheInvalidationKind.Domain,
            Scope = "products",
            Tags = ["domain:products"],
            Domain = "products"
        };

        ClusterPublishResult published = await bus.PublishAsync(command, TestContext.Current.CancellationToken);

        published.AllSucceeded.Should().BeTrue();
        published.Peers.Should().ContainSingle(p => p.PeerId == "b" && p.Succeeded);
        seen.Should().ContainSingle();
        seen[0].Method.Should().Be(HttpMethod.Post);
        seen[0].RequestUri!.ToString().Should().Be("http://127.0.0.1:5002/cache-admin/local/cluster/apply");
        seen[0].Headers.Contains(ClusterEndpointAuth.HeaderName).Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_PeerHttpFailure_ReportsIncomplete()
    {
        List<HttpRequestMessage> seen = [];
        RecordingHandler handler = new(seen, HttpStatusCode.ServiceUnavailable);

        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(HttpClusterCommandBus.HttpClientName)
            .Returns(new HttpClient(handler) { BaseAddress = null });

        IClusterMembership membership = Substitute.For<IClusterMembership>();
        membership.Kind.Returns("Static");
        membership.GetPeersAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ClusterPeer("a", new Uri("http://127.0.0.1:5001")),
            new ClusterPeer("b", new Uri("http://127.0.0.1:5002"))
        ]);

        IInstanceIdProvider instanceId = Substitute.For<IInstanceIdProvider>();
        instanceId.InstanceId.Returns("a");

        IOptionsMonitor<CacheOrchestratorOptions> options = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            Namespace = "app1",
            Admin = { RoutePrefix = "/cache-admin/local", ApiKey = "k" },
            Cluster =
            {
                Bus =
                {
                    Enabled = true,
                    PeerTimeoutMs = 2000,
                    MaxParallelism = 8,
                    Membership = "Static"
                }
            }
        });

        HttpClusterCommandBus bus = new(
            factory,
            membership,
            instanceId,
            options,
            NullLogger<HttpClusterCommandBus>.Instance);

        InvalidateCommand command = new()
        {
            CommandId = Guid.NewGuid(),
            OriginInstanceId = "a",
            Namespace = "app1",
            TimestampUtc = DateTimeOffset.UtcNow,
            Kind = CacheInvalidationKind.Domain,
            Scope = "products",
            Tags = ["domain:products"],
            Domain = "products"
        };

        ClusterPublishResult published = await bus.PublishAsync(command, TestContext.Current.CancellationToken);

        published.AllSucceeded.Should().BeFalse();
        published.Peers.Should().ContainSingle(p => p.PeerId == "b" && !p.Succeeded && p.Error != null);
    }

    private sealed class RecordingHandler(List<HttpRequestMessage> seen, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            seen.Add(request);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
