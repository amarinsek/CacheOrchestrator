using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.UnitTests.Cluster;

public class DefaultClusterCommandHandlerTests
{
    private readonly ICacheOrchestratorInvalidator _invalidator = Substitute.For<ICacheOrchestratorInvalidator>();
    private readonly IInstanceIdProvider _instanceId = Substitute.For<IInstanceIdProvider>();
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
    private readonly DefaultClusterCommandHandler _sut;

    public DefaultClusterCommandHandlerTests()
    {
        _instanceId.InstanceId.Returns("local-1");
        _options.CurrentValue.Returns(new CacheOrchestratorOptions { Namespace = "app1" });
        _sut = new DefaultClusterCommandHandler(
            _invalidator,
            _instanceId,
            _options,
            NullLogger<DefaultClusterCommandHandler>.Instance);
    }

    [Fact]
    public async Task ApplyLocalAsync_WhenNamespaceMismatch_DoesNotInvalidate()
    {
        InvalidateCommand cmd = CreateCommand(ns: "other", origin: "remote-2");

        await _sut.ApplyLocalAsync(cmd, TestContext.Current.CancellationToken);

        await _invalidator.DidNotReceive()
            .InvalidateDomainAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyLocalAsync_WhenOriginIsSelf_DoesNotInvalidate()
    {
        InvalidateCommand cmd = CreateCommand(ns: "app1", origin: "local-1");

        await _sut.ApplyLocalAsync(cmd, TestContext.Current.CancellationToken);

        await _invalidator.DidNotReceive()
            .InvalidateDomainAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyLocalAsync_Domain_CallsInvalidatorUnderRemoteScope()
    {
        InvalidateCommand cmd = CreateCommand(ns: "app1", origin: "remote-2") with
        {
            Kind = CacheInvalidationKind.Domain,
            Domain = "products",
            Scope = "products",
            Tags = ["domain:products"]
        };

        bool sawRemote = false;
        _invalidator
            .InvalidateDomainAsync("products", Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                sawRemote = ClusterCommandScope.IsRemote;
                return ValueTask.FromResult(
                    new CacheInvalidationResult("products", ["domain:products"], true, true, []));
            });

        await _sut.ApplyLocalAsync(cmd, TestContext.Current.CancellationToken);

        sawRemote.Should().BeTrue();
        await _invalidator.Received(1).InvalidateDomainAsync("products", Arg.Any<CancellationToken>());
        ClusterCommandScope.IsRemote.Should().BeFalse();
    }

    private static InvalidateCommand CreateCommand(string ns, string origin) => new()
    {
        CommandId = Guid.NewGuid(),
        OriginInstanceId = origin,
        Namespace = ns,
        TimestampUtc = DateTimeOffset.UtcNow,
        Kind = CacheInvalidationKind.Domain,
        Scope = "products",
        Tags = ["domain:products"],
        Domain = "products"
    };
}
