using CacheOrchestrator.Configuration;
using CacheOrchestrator.Invalidation;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.UnitTests.Invalidation;

public class CacheOrchestratorInvalidatorTests
{
    private readonly IFusionCache _defaultFusion = Substitute.For<IFusionCache>();
    private readonly IFusionCacheProvider _fusionProvider = Substitute.For<IFusionCacheProvider>();
    private readonly IDomainCacheOptionsProvider _domainOptionsProvider = Substitute.For<IDomainCacheOptionsProvider>();
    private readonly IOutputCacheStore _outputCacheStore = Substitute.For<IOutputCacheStore>();
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
    private readonly List<ICacheInvalidationObserver> _observers = [];
    private readonly CacheOrchestratorInvalidator _sut;

    public CacheOrchestratorInvalidatorTests()
    {
        _domainOptionsProvider
            .GetOrCreateDomainOptions(Arg.Any<string>())
            .Returns(callInfo =>
            {
                string domain = callInfo.Arg<string>();
                return new DomainCacheOptions { Domain = domain, FusionCacheInstanceName = "default" };
            });

        _fusionProvider.GetCache("default").Returns(_defaultFusion);

        _options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            FusionCacheInstances = new Dictionary<string, CacheOrchestratorOptions.FusionCacheInstanceOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new(),
                ["pii"] = new()
            }
        });

        _sut = new CacheOrchestratorInvalidator(
            _fusionProvider,
            _domainOptionsProvider,
            _outputCacheStore,
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

        result.Succeeded.Should().BeTrue();
        result.Scope.Should().Be("(skipped)");
        result.Errors.Should().NotBeEmpty();

        await _defaultFusion.DidNotReceiveWithAnyArgs()
            .RemoveByTagAsync(Arg.Any<string>(), Arg.Any<FusionCacheEntryOptions?>(), Arg.Any<CancellationToken>());

        await _outputCacheStore.DidNotReceiveWithAnyArgs()
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
        result.FusionSucceeded.Should().BeTrue();
        result.OutputSucceeded.Should().BeTrue();
        result.Scope.Should().Be("products");
        result.Tags.Should().Equal("domain:products");
        result.Errors.Should().BeEmpty();

        await _defaultFusion.Received(1).RemoveByTagAsync(
            "domain:products",
            Arg.Any<FusionCacheEntryOptions?>(),
            Arg.Any<CancellationToken>());

        await _outputCacheStore.Received(1).EvictByTagAsync(
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
            await _sut.InvalidateEntityAsync("products", "42", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Tags.Should().Equal("entity:products:42");
        result.Scope.Should().Be("products/42");

        await _defaultFusion.Received(1).RemoveByTagAsync(
            "entity:products:42",
            Arg.Any<FusionCacheEntryOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateDomainsAsync_InvalidatesEachDomain()
    {
        CacheInvalidationResult result = await _sut.InvalidateDomainsAsync(
            ["products", "catalog"],
            TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Tags.Should().BeEquivalentTo(["domain:products", "domain:catalog"]);

        await _defaultFusion.Received(1).RemoveByTagAsync(
            "domain:products", Arg.Any<FusionCacheEntryOptions?>(), Arg.Any<CancellationToken>());
        await _defaultFusion.Received(1).RemoveByTagAsync(
            "domain:catalog", Arg.Any<FusionCacheEntryOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateTagsAsync_EvictsOnAllFusionInstances()
    {
        IFusionCache piiFusion = Substitute.For<IFusionCache>();
        _fusionProvider.GetCache("pii").Returns(piiFusion);

        CacheInvalidationResult result =
            await _sut.InvalidateTagsAsync(["custom:tag"], TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Tags.Should().Equal("custom:tag");

        await _defaultFusion.Received(1).RemoveByTagAsync(
            "custom:tag", Arg.Any<FusionCacheEntryOptions?>(), Arg.Any<CancellationToken>());
        await piiFusion.Received(1).RemoveByTagAsync(
            "custom:tag", Arg.Any<FusionCacheEntryOptions?>(), Arg.Any<CancellationToken>());
        await _outputCacheStore.Received(1).EvictByTagAsync("custom:tag", Arg.Any<CancellationToken>());
    }

    // =========================
    // Per-domain FC instance routing
    // =========================

    [Fact]
    public async Task InvalidateDomainAsync_RoutesToCorrectFusionInstance()
    {
        IFusionCache piiFusion = Substitute.For<IFusionCache>();

        _domainOptionsProvider
            .GetOrCreateDomainOptions("users")
            .Returns(new DomainCacheOptions { Domain = "users", FusionCacheInstanceName = "pii" });

        _fusionProvider.GetCache("pii").Returns(piiFusion);

        await _sut.InvalidateDomainAsync("users", TestContext.Current.CancellationToken);

        await piiFusion.Received(1).RemoveByTagAsync(
            "domain:users",
            Arg.Any<FusionCacheEntryOptions?>(),
            Arg.Any<CancellationToken>());

        await _defaultFusion.DidNotReceiveWithAnyArgs()
            .RemoveByTagAsync(Arg.Any<string>(), Arg.Any<FusionCacheEntryOptions?>(), Arg.Any<CancellationToken>());
    }

    // =========================
    // Resilience + partial result
    // =========================

    [Fact]
    public async Task InvalidateDomainAsync_WhenFusionCacheThrows_StillCallsOutputCache_AndReportsPartial()
    {
        _defaultFusion
            .When(x => x.RemoveByTagAsync(
                Arg.Any<string>(),
                Arg.Any<FusionCacheEntryOptions?>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Fusion failed"));

        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.FusionSucceeded.Should().BeFalse();
        result.OutputSucceeded.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Contains("Fusion", StringComparison.OrdinalIgnoreCase));

        await _outputCacheStore.Received(1).EvictByTagAsync(
            "domain:products",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenOutputCacheThrows_StillCallsFusionCache_AndReportsPartial()
    {
        _outputCacheStore
            .When(x => x.EvictByTagAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("OutputCache failed"));

        CacheInvalidationResult result =
            await _sut.InvalidateDomainAsync("products", TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.FusionSucceeded.Should().BeTrue();
        result.OutputSucceeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("OutputCache", StringComparison.OrdinalIgnoreCase));

        await _defaultFusion.Received(1).RemoveByTagAsync(
            "domain:products",
            Arg.Any<FusionCacheEntryOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateDomainAsync_WhenBothThrow_DoesNotPropagateException()
    {
        _defaultFusion
            .When(x => x.RemoveByTagAsync(
                Arg.Any<string>(),
                Arg.Any<FusionCacheEntryOptions?>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Fusion failed"));

        _outputCacheStore
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
        await _defaultFusion.Received(1).RemoveByTagAsync(
            "domain:products", Arg.Any<FusionCacheEntryOptions?>(), Arg.Any<CancellationToken>());
    }
}
