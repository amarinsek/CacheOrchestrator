using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Core.UnitTests.Cluster;

public class InvalidatorClusterPublishTests
{
    private readonly IDataCacheProvider _dataCache = Substitute.For<IDataCacheProvider>();
    private readonly IHttpCacheInvalidationSink _httpCache = Substitute.For<IHttpCacheInvalidationSink>();
    private readonly IDomainCacheOptionsProvider _domainOptionsProvider = Substitute.For<IDomainCacheOptionsProvider>();
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
    private readonly IClusterCommandBus _bus = Substitute.For<IClusterCommandBus>();
    private readonly IInstanceIdProvider _instanceId = Substitute.For<IInstanceIdProvider>();
    private readonly ClusterCommandFactory _factory;

    public InvalidatorClusterPublishTests()
    {
        _dataCache.Name.Returns("FusionCache");
        _domainOptionsProvider
            .GetOrCreateDomainOptions(Arg.Any<string>())
            .Returns(new DomainCacheOptions { Domain = "products", DataCacheInstanceName = "default" });

        _options.CurrentValue.Returns(new CacheOrchestratorOptions { Namespace = "app1" });
        _instanceId.InstanceId.Returns("origin-1");
        _factory = new ClusterCommandFactory(_instanceId, _options);
        _bus.PublishAsync(Arg.Any<ClusterCommand>(), Arg.Any<CancellationToken>())
            .Returns(ClusterPublishResult.Empty);
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenBusEnabled_PublishesCommand()
    {
        _bus.IsEnabled.Returns(true);
        CacheOrchestratorInvalidator sut = CreateSut();

        await sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        await _bus.Received(1).PublishAsync(
            Arg.Is<InvalidateCommand>(c =>
                c.Kind == CacheInvalidationKind.Domain
                && c.Domain == "products"
                && c.Namespace == "app1"
                && c.OriginInstanceId == "origin-1"
                && c.Tags.SequenceEqual(new[] { "domain:products" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenBusDisabled_DoesNotPublish()
    {
        _bus.IsEnabled.Returns(false);
        CacheOrchestratorInvalidator sut = CreateSut();

        await sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        await _bus.DidNotReceive()
            .PublishAsync(Arg.Any<ClusterCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenRemoteScope_DoesNotPublish()
    {
        _bus.IsEnabled.Returns(true);
        CacheOrchestratorInvalidator sut = CreateSut();

        using (ClusterCommandScope.EnterRemote())
        {
            await sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);
        }

        await _bus.DidNotReceive()
            .PublishAsync(Arg.Any<ClusterCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenLocalOnlyScope_DoesNotPublish()
    {
        _bus.IsEnabled.Returns(true);
        CacheOrchestratorInvalidator sut = CreateSut();

        using (ClusterCommandScope.EnterLocalOnly())
        {
            await sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);
        }

        await _bus.DidNotReceive()
            .PublishAsync(Arg.Any<ClusterCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenPublishThrows_StillReturnsLocalSuccessWithClusterFailure()
    {
        _bus.IsEnabled.Returns(true);
        _bus.PublishAsync(Arg.Any<ClusterCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<ClusterPublishResult>>(_ => throw new InvalidOperationException("peer down"));

        CacheOrchestratorInvalidator sut = CreateSut();
        CacheInvalidationResult result =
            await sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue("local Fusion/Output already applied");
        result.ClusterPublish.Should().NotBeNull();
        result.ClusterPublish.AllSucceeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("peer down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenPeerFails_AttachesClusterPublish()
    {
        _bus.IsEnabled.Returns(true);
        _bus.PublishAsync(Arg.Any<ClusterCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ClusterPublishResult(
            [
                new ClusterPeerPublishOutcome
                {
                    PeerId = "b",
                    Succeeded = false,
                    Error = "HTTP 503",
                },
            ]));

        CacheOrchestratorInvalidator sut = CreateSut();
        CacheInvalidationResult result =
            await sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.ClusterPublish!.AllSucceeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Cluster peer 'b'", StringComparison.Ordinal));
    }

    private CacheOrchestratorInvalidator CreateSut()
    {
        return new CacheOrchestratorInvalidator(
            _dataCache,
            _domainOptionsProvider,
            _httpCache,
            _options,
            observers: [],
            NullLogger<CacheOrchestratorInvalidator>.Instance,
            adminStats: null,
            clusterBus: _bus,
            clusterCommands: _factory);
    }
}
