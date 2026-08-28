using CacheOrchestrator.Configuration;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Core.UnitTests.Invalidation;

public class CacheOrchestratorInvalidatorTests
{
    private readonly IDataCacheProvider _dataCache = Substitute.For<IDataCacheProvider>();
    private readonly IHttpCacheInvalidationSink _httpCache = Substitute.For<IHttpCacheInvalidationSink>();
    private readonly IDomainCacheOptionsProvider _domainOptionsProvider = Substitute.For<IDomainCacheOptionsProvider>();
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
    private readonly List<ICacheInvalidationObserver> _observers = [];
    private readonly CacheOrchestratorInvalidator _sut;

    public CacheOrchestratorInvalidatorTests()
    {
        _dataCache.Name.Returns("FusionCache");

        _domainOptionsProvider
            .GetOrCreateDomainOptions(Arg.Any<string>())
            .Returns(callInfo =>
            {
                string domain = callInfo.Arg<string>();
                return new DomainCacheOptions { Domain = domain, DataCacheInstanceName = "default" };
            });

        _options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            DataCacheInstances = new Dictionary<string, CacheOrchestratorOptions.DataCacheInstanceOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new(),
                ["pii"] = new()
            }
        });

        _sut = new CacheOrchestratorInvalidator(
            _dataCache,
            _domainOptionsProvider,
            _httpCache,
            _options,
            _observers,
            NullLogger<CacheOrchestratorInvalidator>.Instance);
    }

    // =========================
    // Input validation / skip
    // =========================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidateDomainAsync_WhenDomainIsNullOrWhitespace_ReturnsSkipped(string? domain)
    {
        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync(domain!, TestContext.Current.CancellationToken);

        result.IsSkipped.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Scope.Should().Be("(skipped)");
        result.Errors.Should().NotBeEmpty();

        await _dataCache.DidNotReceiveWithAnyArgs().InvalidateAsync(
            default!, TestContext.Current.CancellationToken);

        await _httpCache.DidNotReceiveWithAnyArgs()
            .EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // =========================
    // Normal path + result
    // =========================

    [Fact]
    public async Task InvalidateDomainAsync_CallsFusionAndOutput_AndReturnsSuccess()
    {
        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.DataCacheSucceeded.Should().BeTrue();
        result.OutputSucceeded.Should().BeTrue();
        result.Scope.Should().Be("products");
        result.Tags.Should().Equal("domain:products");
        result.Errors.Should().BeEmpty();

        await AssertDataInvalidated("default", "domain:products");

        await _httpCache.Received(1).EvictByTagAsync(
            "domain:products",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateDomainAsync_NormalizesDomain()
    {
        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync("  Product-Catalog  ", TestContext.Current.CancellationToken);

        result.Tags.Should().Equal("domain:product-catalog");
        result.Scope.Should().Be("product-catalog");
    }

    [Fact]
    public async Task InvalidateEntityAsync_EvictsEntityTagOnCorrectInstance()
    {
        CacheInvalidationResult result =
            await _sut.InvalidateEntityAsync("store", "products", "42", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Tags.Should().Equal("entity:store:products:42");
        result.Scope.Should().Be("store/products/42");

        await AssertDataInvalidated("default", "entity:store:products:42");

        await _httpCache.Received(1).EvictByTagAsync(
            "entity:store:products:42",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateEntitiesAsync_EvictsEachEntityTagOnce()
    {
        CacheInvalidationResult result = await _sut.InvalidateEntitiesAsync(
            "store",
            "products",
            ["42", " 42 ", "7", ""],
            TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Scope.Should().Be("store/products");
        result.Tags.Should().Equal("entity:store:products:42", "entity:store:products:7");

        await AssertDataInvalidated(
            "default",
            "entity:store:products:42",
            "entity:store:products:7");

        await _httpCache.Received(1).EvictByTagAsync(
            "entity:store:products:42", Arg.Any<CancellationToken>());
        await _httpCache.Received(1).EvictByTagAsync(
            "entity:store:products:7", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateEntityKindAsync_EvictsKindTag()
    {
        CacheInvalidationResult result =
            await _sut.InvalidateEntityKindAsync("store", "products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Tags.Should().Equal("entitykind:store:products");
        result.Scope.Should().Be("store/products");

        await AssertDataInvalidated("default", "entitykind:store:products");

        await _httpCache.Received(1).EvictByTagAsync(
            "entitykind:store:products",
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("---")]
    public async Task InvalidateEntityAsync_WhenEntityKindIsGarbage_ReturnsSkipped(string kind)
    {
        CacheInvalidationResult result =
            await _sut.InvalidateEntityAsync("store", kind, "42", TestContext.Current.CancellationToken);

        result.IsSkipped.Should().BeTrue();
        result.Succeeded.Should().BeFalse();

        await _dataCache.DidNotReceiveWithAnyArgs().InvalidateAsync(
            default!, TestContext.Current.CancellationToken);
        await _httpCache.DidNotReceiveWithAnyArgs()
            .EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateEntityKindAsync_WhenEntityKindIsGarbage_ReturnsSkipped()
    {
        CacheInvalidationResult result =
            await _sut.InvalidateEntityKindAsync("store", "!!!", TestContext.Current.CancellationToken);

        result.IsSkipped.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task InvalidateDomainsAsync_WhenNoDomains_ReturnsSkippedNotSucceeded()
    {
        CacheInvalidationResult result = await _sut.InvalidateDomainsAsync(
            ["", "  "],
            TestContext.Current.CancellationToken);

        result.IsSkipped.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Scope.Should().Be("(skipped)");
    }

    [Fact]
    public async Task InvalidateDomainsAsync_InvalidatesEachDomain()
    {
        CacheInvalidationResult result = await _sut.InvalidateDomainsAsync(
            ["products", "catalog"],
            TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Tags.Should().BeEquivalentTo(["domain:products", "domain:catalog"]);

        await AssertDataInvalidated("default", "domain:products");
        await AssertDataInvalidated("default", "domain:catalog");
    }

    [Fact]
    public async Task InvalidateTagsAsync_UsesBatchCapabilityAcrossNamedInstances()
    {
        IDataCacheProvider provider = Substitute.For<IDataCacheProvider, IDataCacheBatchInvalidator>();
        provider.Name.Returns("BatchProvider");
        IDataCacheBatchInvalidator batch = (IDataCacheBatchInvalidator)provider;
        batch.InvalidateBatchAsync(Arg.Any<IReadOnlyList<DataCacheInvalidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        CacheOrchestratorInvalidator sut = new(
            provider,
            _domainOptionsProvider,
            _httpCache,
            _options,
            _observers,
            NullLogger<CacheOrchestratorInvalidator>.Instance);

        CacheInvalidationResult result = await sut.InvalidateTagsAsync(
            ["domain:products", "entitykind:products:items"],
            TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        await batch.Received(1).InvalidateBatchAsync(
            Arg.Is<IReadOnlyList<DataCacheInvalidationRequest>>(requests =>
                requests.Count == 2
                && requests.Select(request => request.InstanceName).SequenceEqual(new[] { "default", "pii" })
                && requests.All(request => request.Tags.Count == 2)),
            Arg.Any<CancellationToken>());
        await provider.DidNotReceiveWithAnyArgs().InvalidateAsync(
            default!,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InvalidateTagsAsync_EvictsOnAllFusionInstances()
    {
        CacheInvalidationResult result =
            await _sut.InvalidateTagsAsync(["custom:tag"], TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Tags.Should().Equal("custom:tag");

        await AssertDataInvalidated("default", "custom:tag");
        await AssertDataInvalidated("pii", "custom:tag");
        await _httpCache.Received(1).EvictByTagAsync("custom:tag", Arg.Any<CancellationToken>());
    }

    // =========================
    // Per-domain FC instance routing
    // =========================

    [Fact]
    public async Task InvalidateDomainAsync_RoutesToCorrectFusionInstance()
    {
        _domainOptionsProvider
            .GetOrCreateDomainOptions("users")
            .Returns(new DomainCacheOptions { Domain = "users", DataCacheInstanceName = "pii" });

        await _sut.InvalidateDomainAsync("users", TestContext.Current.CancellationToken);

        await AssertDataInvalidated("pii", "domain:users");

        await AssertDataNotInvalidated("default");
    }

    // =========================
    // Resilience + partial result
    // =========================

    [Fact]
    public async Task InvalidateDomainAsync_WhenFusionCacheThrows_StillCallsOutputCache_AndReportsPartial()
    {
        _dataCache
            .When(x => x.InvalidateAsync(
                Arg.Any<DataCacheInvalidationRequest>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Fusion failed"));

        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.DataCacheSucceeded.Should().BeFalse();
        result.OutputSucceeded.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Contains("Fusion", StringComparison.OrdinalIgnoreCase));

        await _httpCache.Received(1).EvictByTagAsync(
            "domain:products",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenOutputCacheThrows_StillCallsFusionCache_AndReportsPartial()
    {
        _httpCache
            .When(x => x.EvictByTagAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("OutputCache failed"));

        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.DataCacheSucceeded.Should().BeTrue();
        result.OutputSucceeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("OutputCache", StringComparison.OrdinalIgnoreCase));

        await AssertDataInvalidated("default", "domain:products");
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenBothThrow_DoesNotPropagateException()
    {
        _dataCache
            .When(x => x.InvalidateAsync(
                Arg.Any<DataCacheInvalidationRequest>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Fusion failed"));

        _httpCache
            .When(x => x.EvictByTagAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("OutputCache failed"));

        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.Errors.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    // =========================
    // Observers
    // =========================

    [Fact]
    public async Task InvalidateDomainAsync_NotifiesObservers_BeforeAndAfter()
    {
        ICacheInvalidationObserver observer = Substitute.For<ICacheInvalidationObserver>();
        _observers.Add(observer);

        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        await observer.Received(1).OnBeforeInvalidateAsync(
            Arg.Is<CacheInvalidationContext>(c =>
                c.Kind == CacheInvalidationKind.Domain
                && c.Scope == "products"
                && c.Tags.SequenceEqual(new[] { "domain:products" })),
            Arg.Any<CancellationToken>());

        await observer.Received(1).OnAfterInvalidateAsync(
            Arg.Any<CacheInvalidationContext>(),
            Arg.Is<CacheInvalidationResult>(r => r.Succeeded && r.Scope == "products"),
            Arg.Any<CancellationToken>());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenObserverThrows_StillCompletesInvalidation()
    {
        ICacheInvalidationObserver observer = Substitute.For<ICacheInvalidationObserver>();
        observer
            .When(o => o.OnBeforeInvalidateAsync(Arg.Any<CacheInvalidationContext>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("observer boom"));
        _observers.Add(observer);

        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        await AssertDataInvalidated("default", "domain:products");
    }

    [Fact]
    public async Task InvalidateDomainsAsync_NotifiesObserversWithDomainsKind()
    {
        ICacheInvalidationObserver observer = Substitute.For<ICacheInvalidationObserver>();
        _observers.Add(observer);

        CacheInvalidationResult result = await _sut.InvalidateDomainsAsync(
            ["products", "catalog"],
            TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();

        await observer.Received(1).OnBeforeInvalidateAsync(
            Arg.Is<CacheInvalidationContext>(c =>
                c.Kind == CacheInvalidationKind.Domains
                && c.Tags.Contains("domain:products")
                && c.Tags.Contains("domain:catalog")),
            Arg.Any<CancellationToken>());

        await observer.Received(1).OnAfterInvalidateAsync(
            Arg.Is<CacheInvalidationContext>(c => c.Kind == CacheInvalidationKind.Domains),
            Arg.Is<CacheInvalidationResult>(r => r.Succeeded),
            Arg.Any<CancellationToken>());

        await observer.Received(1).OnBeforeInvalidateAsync(
            Arg.Is<CacheInvalidationContext>(c => c.Kind == CacheInvalidationKind.Domain && c.Scope == "products"),
            Arg.Any<CancellationToken>());
        await observer.Received(1).OnBeforeInvalidateAsync(
            Arg.Is<CacheInvalidationContext>(c => c.Kind == CacheInvalidationKind.Domain && c.Scope == "catalog"),
            Arg.Any<CancellationToken>());
    }

    private async Task AssertDataInvalidated(string instanceName, params string[] tags)
    {
        await _dataCache.Received(1).InvalidateAsync(
            Arg.Is<DataCacheInvalidationRequest>(request =>
                request.InstanceName == instanceName && request.Tags.SequenceEqual(tags)),
            Arg.Any<CancellationToken>());
    }

    private async Task AssertDataNotInvalidated(string instanceName)
    {
        await _dataCache.DidNotReceive().InvalidateAsync(
            Arg.Is<DataCacheInvalidationRequest>(request => request.InstanceName == instanceName),
            Arg.Any<CancellationToken>());
    }
}
