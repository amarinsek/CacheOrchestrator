using CacheOrchestrator.Edge.Configuration;
using CacheOrchestrator.Edge.Invalidation;
using CacheOrchestrator.Edge.Providers;
using CacheOrchestrator.Edge.Responses;
using CacheOrchestrator.Edge.Tags;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Edge.UnitTests;

public class EdgeIntegrationTests
{
    [Fact]
    public async Task ResponseContributor_ProjectsTagsAndFreshness()
    {
        TestProvider provider = new();
        (EdgeResponseContributor sut, _, _) = Create(provider);
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Get;
        var context = new CacheResponseContext(
            http,
            new DomainHttpCacheOptions { CoreOptions = new DomainCacheOptions { Domain = "catalog" } },
            sharedCacheEligible: true,
            ["domain:catalog", "entity:catalog:products:42"]);

        await sut.ContributeAsync(context, TestContext.Current.CancellationToken);

        provider.Metadata.Should().NotBeNull();
        provider.Metadata!.IsCacheable.Should().BeTrue();
        provider.Metadata.Ttl.Should().Be(TimeSpan.FromMinutes(10));
        provider.Metadata.StaleWhileRevalidate.Should().Be(TimeSpan.FromSeconds(30));
        provider.Metadata.Tags.Should().HaveCount(2).And.OnlyContain(tag => tag.StartsWith("coe1-"));
    }

    [Fact]
    public async Task ResponseContributor_WhenProviderBudgetExceeded_DisablesSharedCaching()
    {
        TestProvider provider = new(maxResponseTagBytes: 1);
        (EdgeResponseContributor sut, _, _) = Create(provider);
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Get;
        var context = new CacheResponseContext(
            http,
            new DomainHttpCacheOptions { CoreOptions = new DomainCacheOptions { Domain = "catalog" } },
            sharedCacheEligible: true,
            ["domain:catalog", "entity:catalog:products:42"]);

        await sut.ContributeAsync(context, TestContext.Current.CancellationToken);

        provider.Metadata!.IsCacheable.Should().BeFalse();
        provider.Metadata.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task ResponseContributor_OnOutputCacheHit_RebuildsProviderHeadersFromStoredTags()
    {
        TestProvider provider = new();
        (EdgeResponseContributor sut, _, _) = Create(provider);
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Get;
        var context = new CacheResponseContext(
            http,
            new DomainHttpCacheOptions { CoreOptions = new DomainCacheOptions { Domain = "catalog" } },
            sharedCacheEligible: true,
            ["domain:catalog"],
            OutputCacheResult.Hit);

        await sut.ContributeAsync(context, TestContext.Current.CancellationToken);

        provider.Metadata.Should().NotBeNull();
        provider.Metadata!.Tags.Should().ContainSingle();
    }

    [Fact]
    public async Task ResponseContributor_HeadRequest_RemainsEdgeCacheable()
    {
        TestProvider provider = new();
        (EdgeResponseContributor sut, _, _) = Create(provider);
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Head;
        var context = new CacheResponseContext(
            http,
            new DomainHttpCacheOptions { CoreOptions = new DomainCacheOptions { Domain = "catalog" } },
            sharedCacheEligible: true,
            ["domain:catalog"]);

        await sut.ContributeAsync(context, TestContext.Current.CancellationToken);

        provider.Metadata.Should().NotBeNull();
        provider.Metadata!.IsCacheable.Should().BeTrue();
        provider.Metadata.Tags.Should().ContainSingle();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public async Task ResponseContributor_NonGetOrHeadRequest_DisablesEdgeStorage(string method)
    {
        TestProvider provider = new();
        (EdgeResponseContributor sut, _, _) = Create(provider);
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        var context = new CacheResponseContext(
            http,
            new DomainHttpCacheOptions { CoreOptions = new DomainCacheOptions { Domain = "catalog" } },
            sharedCacheEligible: true,
            ["domain:catalog"]);

        await sut.ContributeAsync(context, TestContext.Current.CancellationToken);

        provider.Metadata.Should().NotBeNull();
        provider.Metadata!.IsCacheable.Should().BeFalse();
        provider.Metadata.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidationObserver_LocalEntity_EnqueuesProjectedTag()
    {
        TestProvider provider = new();
        (_, EdgeInvalidationObserver sut, RecordingQueue queue) = Create(provider);
        var context = new CacheInvalidationContext(
            CacheInvalidationKind.Entity,
            "catalog/products/42",
            ["entity:catalog:products:42"],
            CacheInvalidationOrigin.Local);
        var result = new CacheInvalidationResult("catalog/products/42", context.Tags, true, true);

        await sut.OnAfterInvalidateAsync(context, result, TestContext.Current.CancellationToken);

        queue.Jobs.Should().ContainSingle();
        queue.Jobs[0].Tags.Should().ContainSingle().Which.Should().StartWith("coe1-");
    }

    [Fact]
    public async Task InvalidationObserver_RemoteCluster_DoesNotEnqueue()
    {
        TestProvider provider = new();
        (_, EdgeInvalidationObserver sut, RecordingQueue queue) = Create(provider);
        var context = new CacheInvalidationContext(
            CacheInvalidationKind.Domain,
            "catalog",
            ["domain:catalog"],
            CacheInvalidationOrigin.RemoteCluster);
        var result = new CacheInvalidationResult("catalog", context.Tags, true, true);

        await sut.OnAfterInvalidateAsync(context, result, TestContext.Current.CancellationToken);

        queue.Jobs.Should().BeEmpty();
    }

    private static (EdgeResponseContributor Response, EdgeInvalidationObserver Observer, RecordingQueue Queue) Create(
        TestProvider provider)
    {
        CacheOrchestratorEdgeOptions edgeOptions = new()
        {
            EdgeInstances = new Dictionary<string, EdgeInstanceOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["edge"] = new() { Provider = provider.Name, Namespace = "test" }
            },
            Domains = new Dictionary<string, EdgeDomainContainer>(StringComparer.OrdinalIgnoreCase)
            {
                ["catalog"] = new()
                {
                    Edge = new DomainEdgeSettings
                    {
                        Enabled = true,
                        Instance = "edge",
                        TtlSeconds = 600,
                        StaleWhileRevalidateSeconds = 30
                    }
                }
            }
        };
        IOptionsMonitor<CacheOrchestratorEdgeOptions> edgeMonitor = Substitute.For<IOptionsMonitor<CacheOrchestratorEdgeOptions>>();
        edgeMonitor.CurrentValue.Returns(edgeOptions);
        IOptionsMonitor<CacheOrchestratorOptions> coreMonitor = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        coreMonitor.CurrentValue.Returns(new CacheOrchestratorOptions { Namespace = "app" });
        var catalog = new EdgeProviderCatalog([provider], [provider]);
        var instances = new EdgeInstanceResolver(edgeMonitor, coreMonitor, catalog);
        var domainOptions = new DomainEdgeOptionsProvider(edgeMonitor);
        var projector = new EdgeTagProjector();
        var queue = new RecordingQueue();
        return (
            new EdgeResponseContributor(domainOptions, instances, projector, NullLogger<EdgeResponseContributor>.Instance),
            new EdgeInvalidationObserver(domainOptions, instances, projector, queue),
            queue);
    }

    private sealed class RecordingQueue : IEdgeInvalidationQueue
    {
        public List<EdgeInvalidationJob> Jobs { get; } = [];

        public ValueTask EnqueueAsync(EdgeInvalidationJob job, CancellationToken cancellationToken)
        {
            Jobs.Add(job);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestProvider(int maxResponseTagBytes = 16 * 1024)
        : IEdgeResponseProvider, IEdgeInvalidationProvider
    {
        public string Name => "Test";
        public EdgeProviderCapabilities Capabilities { get; } = new()
        {
            SupportsTagInvalidation = true,
            MaxResponseTagBytes = maxResponseTagBytes,
            MaxInvalidationBatchSize = 100,
            SupportsStaleWhileRevalidate = true,
            SupportsStaleIfError = true
        };
        public EdgeResponseMetadata? Metadata { get; private set; }
        public void ApplyResponseMetadata(HttpResponse response, EdgeResponseMetadata metadata) => Metadata = metadata;
        public ValueTask<EdgeInvalidationResult> InvalidateAsync(EdgeInvalidationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(EdgeInvalidationResult.Success);
    }
}
