using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.UnitTests.Fusion;

public class DomainFusionCacheServiceTests
{
    private readonly IFusionCacheProvider _fusionProvider = Substitute.For<IFusionCacheProvider>();
    private readonly IFusionCache _fusionCache = Substitute.For<IFusionCache>();
    private readonly IDomainCacheOptionsProvider _domainConfig = Substitute.For<IDomainCacheOptionsProvider>();
    private readonly IDomainKeyGenerator _keyGenerator = Substitute.For<IDomainKeyGenerator>();
    private readonly DomainFusionCacheService _sut;

    public DomainFusionCacheServiceTests()
    {
        _fusionProvider.GetCache(Arg.Any<string>()).Returns(_fusionCache);
        _sut = new DomainFusionCacheService(
            _fusionProvider,
            _domainConfig,
            _keyGenerator,
            NullLogger<DomainFusionCacheService>.Instance);
    }

    // =========================
    // Disabled / missing config
    // =========================

    [Fact]
    public async Task GetOrSetAsync_WhenConfigIsNull_AndNoEndpointDomain_CallsFactoryDirectly()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Path = "/api/uncached";
        _domainConfig.GetDomainOptions(http).Returns((DomainCacheOptions?)null);

        bool factoryCalled = false;
        int result = await _sut.GetOrSetAsync(http, _ =>
        {
            factoryCalled = true;
            return Task.FromResult(42);
        }, TestContext.Current.CancellationToken);

        result.Should().Be(42);
        factoryCalled.Should().BeTrue();
        _domainConfig.DidNotReceive().EnsureDomainOptions(Arg.Any<HttpContext>(), Arg.Any<string>());

        // Disposition records unresolved so X-Cache can show data=unresolved
        http.Items[CacheOrchestratorKeys.DispositionKey].Should().BeOfType<CacheDisposition>()
            .Which.Data.Should().Be(DataCacheResult.Unresolved);
    }

    [Fact]
    public async Task GetOrSetAsync_WithDomainOverload_WhenConfigMissing_EnsuresDomain()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(domain: "reports");
        _domainConfig.GetDomainOptions(http).Returns((DomainCacheOptions?)null);
        _domainConfig.EnsureDomainOptions(http, "reports").Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");

        try
        {
            await _sut.GetOrSetAsync(http, "reports", _ => Task.FromResult(1), TestContext.Current.CancellationToken);
        }
        catch
        {
            // Fusion substitute may not complete GetOrSetAsync
        }

        _domainConfig.Received(1).EnsureDomainOptions(http, "reports");
        _keyGenerator.Received(1).Generate(cfg, http);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenConfigMissing_ResolvesDomainFromPolicyMetadata()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(domain: "catalog");
        _domainConfig.GetDomainOptions(http).Returns((DomainCacheOptions?)null);
        _domainConfig.EnsureDomainOptions(http, "catalog").Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new DomainOutputCachePolicy("catalog")),
            "test");
        http.SetEndpoint(endpoint);

        try
        {
            await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);
        }
        catch
        {
            // expected with substitute
        }

        _domainConfig.Received(1).EnsureDomainOptions(http, "catalog");
    }

    [Fact]
    public async Task GetOrSetAsync_WhenConfigMissing_ResolvesDomainFromCacheDomainAttribute()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(domain: "orders");
        _domainConfig.GetDomainOptions(http).Returns((DomainCacheOptions?)null);
        _domainConfig.EnsureDomainOptions(http, "orders").Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new CacheDomainAttribute("orders")),
            "test");
        http.SetEndpoint(endpoint);

        try
        {
            await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);
        }
        catch
        {
            // expected
        }

        _domainConfig.Received(1).EnsureDomainOptions(http, "orders");
    }

    [Fact]
    public async Task GetOrSetAsync_WhenConfigAlreadyOnRequest_DoesNotReEnsure()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(domain: "products");
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");

        try
        {
            await _sut.GetOrSetAsync(http, "ignored", _ => Task.FromResult(1), TestContext.Current.CancellationToken);
        }
        catch
        {
            // expected
        }

        _domainConfig.DidNotReceive().EnsureDomainOptions(Arg.Any<HttpContext>(), Arg.Any<string>());
        _keyGenerator.Received(1).Generate(cfg, http);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenFusionCacheDisabled_CallsFactoryDirectly()
    {
        var http = new DefaultHttpContext();
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig(enabled: false));

        bool factoryCalled = false;
        int result = await _sut.GetOrSetAsync(http, _ =>
        {
            factoryCalled = true;
            return Task.FromResult(99);
        }, TestContext.Current.CancellationToken);

        result.Should().Be(99);
        factoryCalled.Should().BeTrue();
    }

    // =========================
    // RespectNoStore
    // =========================

    [Fact]
    public async Task GetOrSetAsync_WhenRespectNoStoreAndNoStoreHeader_CallsFactoryDirectly()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.CacheControl = "no-store";
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig(respectNoStore: true));

        bool factoryCalled = false;
        int result = await _sut.GetOrSetAsync(http, _ =>
        {
            factoryCalled = true;
            return Task.FromResult(7);
        }, TestContext.Current.CancellationToken);

        result.Should().Be(7);
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrSetAsync_WhenRespectNoStoreButHeaderMissing_DoesNotSkipCache()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(respectNoStore: true);
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("test-key");

        // We only verify that the key generator was called (meaning we entered the FusionCache path)
        // Full FusionCache interaction is better covered by integration tests.
        try
        {
            await _sut.GetOrSetAsync(http, _ => Task.FromResult(123), TestContext.Current.CancellationToken);
        }
        catch
        {
            // Expected � our substitute doesn't fully implement GetOrSetAsync
        }

        _keyGenerator.Received(1).Generate(cfg, http);
    }

    // =========================
    // Normal path � key generation & tags
    // =========================

    [Fact]
    public async Task GetOrSetAsync_WhenEnabled_CallsKeyGenerator()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("products:202608011200:abc123");

        try
        {
            await _sut.GetOrSetAsync(http, _ => Task.FromResult("value"), TestContext.Current.CancellationToken);
        }
        catch
        {
            // Expected with incomplete substitute
        }

        _keyGenerator.Received(1).Generate(cfg, http);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenEnabled_UsesDomainFromConfig()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(domain: "orders");
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");

        try
        {
            await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);
        }
        catch
        {
            // Expected
        }

        _keyGenerator.Received(1).Generate(
            Arg.Is<DomainCacheOptions>(c => c.Domain == "orders"),
            http);
    }

    // =========================
    // Fail-safe entry options
    // =========================

    [Fact]
    public async Task GetOrSetAsync_WhenFailSafeDurationPositive_EnablesFailSafeOnEntryOptions()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(failSafe: TimeSpan.FromHours(24));
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");

        EntryOptionsCapture capture = StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        capture.Options.Should().NotBeNull();
        capture.Options!.IsFailSafeEnabled.Should().BeTrue();
        capture.Options.FailSafeMaxDuration.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task GetOrSetAsync_WhenFailSafeDurationZero_DoesNotEnableFailSafe()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(failSafe: TimeSpan.Zero);
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");

        EntryOptionsCapture capture = StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        capture.Options.Should().NotBeNull();
        capture.Options!.IsFailSafeEnabled.Should().BeFalse();
        capture.Options.FailSafeMaxDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetOrSetAsync_ReusesCachedFusionEntryOptionsOnSameDomainSnapshot()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");

        EntryOptionsCapture capture1 = StubGetOrSetAndCaptureOptions(returnValue: 1);
        await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        // Re-stub so the second call also captures options (NSubstitute keeps the previous config).
        EntryOptionsCapture capture2 = StubGetOrSetAndCaptureOptions(returnValue: 2);
        await _sut.GetOrSetAsync(http, _ => Task.FromResult(2), TestContext.Current.CancellationToken);

        capture1.Options.Should().NotBeNull();
        capture2.Options.Should().NotBeNull();
        capture1.Options.Should().BeSameAs(capture2.Options);
        capture1.Options.Should().BeSameAs(cfg.GetFusionEntryOptions());
    }

    /// <summary>
    /// Stubs the core <see cref="IFusionCache.GetOrSetAsync{T}"/> overload (with <see cref="MaybeValue{T}"/>)
    /// that extension methods ultimately call, and captures entry options into a mutable holder.
    /// </summary>
    private EntryOptionsCapture StubGetOrSetAndCaptureOptions(int returnValue)
    {
        EntryOptionsCapture capture = new();

        _fusionCache
            .GetOrSetAsync(
                Arg.Any<string>(),
                Arg.Any<Func<FusionCacheFactoryExecutionContext<int>, CancellationToken, Task<int>>>(),
                Arg.Any<MaybeValue<int>>(),
                Arg.Do<FusionCacheEntryOptions>(o => capture.Options = o),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(returnValue));

        return capture;
    }

    private sealed class EntryOptionsCapture
    {
        public FusionCacheEntryOptions? Options { get; set; }
    }

    // =========================
    // Helpers
    // =========================

    private static DomainCacheOptions CreateConfig(
        string domain = "products",
        bool enabled = true,
        bool respectNoStore = false,
        TimeSpan? failSafe = null) => new()
        {
            Domain = domain,
            FusionCacheEnabled = enabled,
            FusionCacheRespectNoStore = respectNoStore,
            Version = "1",
            FusionCacheSoftTtl = TimeSpan.FromMinutes(5),
            FusionCacheHardTtl = TimeSpan.FromHours(1),
            FusionCacheFailSafe = failSafe ?? TimeSpan.FromHours(24),
            FusionCacheJitterSeconds = 30,
            FusionCacheEagerRefreshRatio = 0.9,
            FusionCacheFactorySoftTimeoutSeconds = 1,
            FusionCacheFactoryHardTimeoutSeconds = 5,
            FusionCacheAllowBackgroundDistributed = true,
            FusionCacheAllowBackgroundBackplane = true
        };
}