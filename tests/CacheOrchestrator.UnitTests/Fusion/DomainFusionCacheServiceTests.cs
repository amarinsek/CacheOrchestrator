using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
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

        // Disposition records unresolved so X-Cache can show fc=unresolved
        http.Features.Get<ICacheOrchestratorFeature>()?.Disposition.Should().BeOfType<CacheDisposition>()
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
        StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetAsync(http, "reports", _ => Task.FromResult(1), TestContext.Current.CancellationToken);

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
        StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

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
        StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        _domainConfig.Received(1).EnsureDomainOptions(http, "orders");
    }

    [Fact]
    public async Task GetOrSetAsync_WhenConfigAlreadyOnRequest_SameDomain_DoesNotReEnsure()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(domain: "products");
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");
        StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetAsync(http, "products", _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        _domainConfig.DidNotReceive().EnsureDomainOptions(Arg.Any<HttpContext>(), Arg.Any<string>());
        _keyGenerator.Received(1).Generate(cfg, http);
    }

    [Fact]
    public async Task GetOrSetAsync_WithDomainOverload_WhenDifferentDomainAlreadyOnRequest_UsesExplicitDomain()
    {
        var http = new DefaultHttpContext();
        var products = CreateConfig(domain: "products");
        var catalog = CreateConfig(domain: "catalog");
        _domainConfig.GetDomainOptions(http).Returns(products);
        _domainConfig.EnsureDomainOptions(http, "catalog").Returns(catalog);
        _keyGenerator.Generate(catalog, http).Returns("key");
        StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetAsync(http, "catalog", _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        _domainConfig.Received(1).EnsureDomainOptions(http, "catalog");
        _keyGenerator.Received(1).Generate(catalog, http);
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
        http.Features.Get<ICacheOrchestratorFeature>()?.Disposition.Should().BeOfType<CacheDisposition>()
            .Which.Data.Should().Be(DataCacheResult.Off);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenFusionCacheDisabled_RecordsFactoryRunOnAdmin()
    {
        var admin = Substitute.For<IAdminStatsCollector>();
        admin.IsEnabled.Returns(true);
        var sut = new DomainFusionCacheService(
            _fusionProvider,
            _domainConfig,
            _keyGenerator,
            NullLogger<DomainFusionCacheService>.Instance,
            admin);
        var http = new DefaultHttpContext();
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig(enabled: false));

        await sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        admin.Received().RecordFusion(
            Arg.Any<string?>(),
            "products",
            "off",
            Arg.Any<long?>(),
            Arg.Any<long?>());
    }

    [Fact]
    public async Task GetOrSetAsync_WhenFusionRespectAuthBypass_AndAuthenticated_CallsFactoryUncached()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "alice")],
            authenticationType: "test"));
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig(fusionRespectAuthBypass: true));

        bool factoryCalled = false;
        int result = await _sut.GetOrSetAsync(http, _ =>
        {
            factoryCalled = true;
            return Task.FromResult(3);
        }, TestContext.Current.CancellationToken);

        result.Should().Be(3);
        factoryCalled.Should().BeTrue();
        _keyGenerator.DidNotReceive().Generate(Arg.Any<DomainCacheOptions>(), Arg.Any<HttpContext>());
        http.Features.Get<ICacheOrchestratorFeature>()?.Disposition.Should().BeOfType<CacheDisposition>()
            .Which.Data.Should().Be(DataCacheResult.Bypass);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenFusionRespectAuthBypassFalse_StillCachesUnderAuthorization()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer token";
        var cfg = CreateConfig(fusionRespectAuthBypass: false);
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");
        StubGetOrSetAndCaptureOptions(returnValue: 8);

        int result = await _sut.GetOrSetAsync(http, _ => Task.FromResult(8), TestContext.Current.CancellationToken);

        result.Should().Be(8);
        _keyGenerator.Received(1).Generate(cfg, http);
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
        http.Features.Get<ICacheOrchestratorFeature>()?.Disposition.Should().BeOfType<CacheDisposition>()
            .Which.Data.Should().Be(DataCacheResult.Bypass);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenRespectNoStoreButHeaderMissing_DoesNotSkipCache()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(respectNoStore: true);
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("test-key");
        StubGetOrSetAndCaptureOptions(returnValue: 123);

        int result = await _sut.GetOrSetAsync(http, _ => Task.FromResult(123), TestContext.Current.CancellationToken);

        result.Should().Be(123);
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
        EntryOptionsCapture capture = StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        _keyGenerator.Received(1).Generate(cfg, http);
        capture.Tags.Should().Equal("domain:products");
    }

    [Fact]
    public async Task GetOrSetAsync_WhenEnabled_UsesDomainFromConfig()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(domain: "orders");
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("key");
        StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

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

#pragma warning disable CS0618 // Obsolete GetOrSetEntityAsync overloads — retained until next major
    [Fact]
    public async Task GetOrSetEntityAsync_SetsIdentityOnlyForTheCall_ThenRestores()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator
            .Generate(cfg, Arg.Any<HttpContext>())
            .Returns(ci =>
            {
                HttpContext ctx = ci.Arg<HttpContext>();
                ctx.Features.Get<ICacheOrchestratorFeature>()?.EntityKind.Should().Be("items");
                ctx.Features.Get<ICacheOrchestratorFeature>()?.ResourceId.Should().Be("42");
                return "entity-key";
            });
        StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetEntityAsync(
            http,
            "items",
            "42",
            _ => Task.FromResult(1),
            TestContext.Current.CancellationToken);

        (http.Features.Get<ICacheOrchestratorFeature>()?.EntityKind).Should().BeNull();
        (http.Features.Get<ICacheOrchestratorFeature>()?.ResourceId).Should().BeNull();
    }

    [Fact]
    public async Task GetOrSetEntityAsync_RestoresPreviousIdentityFromOutputCache()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "products", ResourceId = "99" });
        _keyGenerator.Generate(cfg, Arg.Any<HttpContext>()).Returns("entity-key");
        StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetEntityAsync(
            http,
            "items",
            "42",
            _ => Task.FromResult(1),
            TestContext.Current.CancellationToken);

        http.Features.Get<ICacheOrchestratorFeature>()?.EntityKind.Should().Be("products");
        http.Features.Get<ICacheOrchestratorFeature>()?.ResourceId.Should().Be("99");
    }

    [Fact]
    public async Task GetOrSetEntityAsync_PassesDomainEntityAndKindTags()
    {
        var http = new DefaultHttpContext();
        var cfg = CreateConfig(domain: "store");
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, Arg.Any<HttpContext>()).Returns("entity-key");
        EntryOptionsCapture capture = StubGetOrSetAndCaptureOptions(returnValue: 1);

        await _sut.GetOrSetEntityAsync(
            http,
            "items",
            "42",
            _ => Task.FromResult(1),
            TestContext.Current.CancellationToken);

        capture.Tags.Should().Equal(
            "domain:store",
            "entity:store:items:42",
            "entitykind:store:items");
    }

    [Fact]
    public async Task GetOrSetEntityAsync_WhenEntityKindIsGarbage_Throws()
    {
        var http = new DefaultHttpContext();
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig());

        var act = () => _sut.GetOrSetEntityAsync(
            http,
            "!!!",
            "42",
            _ => Task.FromResult(1),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("entityKind");
    }

    [Fact]
    public async Task GetOrSetEntityAsync_WhenResourceIdIsGarbage_Throws()
    {
        var http = new DefaultHttpContext();
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig());

        var act = () => _sut.GetOrSetEntityAsync(
            http,
            "items",
            "---",
            _ => Task.FromResult(1),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("resourceId");
    }

    [Fact]
    public async Task GetOrSetEntityAsync_KeyIgnoresPathAndQuery()
    {
        var cfg = CreateConfig(domain: "store");
        _domainConfig.GetDomainOptions(Arg.Any<HttpContext>()).Returns(cfg);

        var realKeys = new DefaultDomainKeyGenerator();
        var sut = new DomainFusionCacheService(
            _fusionProvider,
            _domainConfig,
            realKeys,
            NullLogger<DomainFusionCacheService>.Instance);

        var keys = new List<string>();
        _fusionCache
            .GetOrSetAsync(
                Arg.Any<string>(),
                Arg.Any<Func<FusionCacheFactoryExecutionContext<int>, CancellationToken, Task<int>>>(),
                Arg.Any<MaybeValue<int>>(),
                Arg.Any<FusionCacheEntryOptions>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                keys.Add(ci.Arg<string>());
                return ValueTask.FromResult(1);
            });

        var httpA = new DefaultHttpContext();
        httpA.Request.Path = "/api/a";
        httpA.Request.QueryString = new QueryString("?page=1");
        var httpB = new DefaultHttpContext();
        httpB.Request.Path = "/api/b";
        httpB.Request.QueryString = new QueryString("?page=99");

        await sut.GetOrSetEntityAsync(httpA, "items", "42", _ => Task.FromResult(1), TestContext.Current.CancellationToken);
        await sut.GetOrSetEntityAsync(httpB, "items", "42", _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        keys.Should().HaveCount(2);
        keys[0].Should().Be(keys[1]);
        keys[0].Should().Contain(":id:items:42:");
    }

    [Fact]
    public async Task GetOrSetAsync_AfterGetOrSetEntityAsync_DoesNotUseEntityKeyShape()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Path = "/api/products";
        var cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);

        var realKeys = new DefaultDomainKeyGenerator();
        var sut = new DomainFusionCacheService(
            _fusionProvider,
            _domainConfig,
            realKeys,
            NullLogger<DomainFusionCacheService>.Instance);

        var keys = new List<string>();
        _fusionCache
            .GetOrSetAsync(
                Arg.Any<string>(),
                Arg.Any<Func<FusionCacheFactoryExecutionContext<int>, CancellationToken, Task<int>>>(),
                Arg.Any<MaybeValue<int>>(),
                Arg.Any<FusionCacheEntryOptions>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                keys.Add(ci.Arg<string>());
                return ValueTask.FromResult(1);
            });

        await sut.GetOrSetEntityAsync(
            http,
            "items",
            "42",
            _ => Task.FromResult(1),
            TestContext.Current.CancellationToken);
        await sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        keys.Should().HaveCount(2);
        keys[0].Should().Contain(":id:items:42:");
        keys[1].Should().NotContain(":id:items:42:");
    }
#pragma warning restore CS0618

    [Fact]
    public async Task GetOrSetEntityAsync_FromRequestIdentity_UsesPrimaryTags()
    {
        var http = new DefaultHttpContext();
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "items", ResourceId = "42" });
        DomainCacheOptions cfg = CreateConfig(domain: "store");
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, Arg.Any<HttpContext>()).Returns("entity-key");

        FootprintCapture capture = StubFootprintGetOrSet();

        string? result = await _sut.GetOrSetEntityAsync(
            http,
            _ => Task.FromResult<string?>("ok"),
            TestContext.Current.CancellationToken);

        result.Should().Be("ok");
        capture.SetTags.Should().Contain("entity:store:items:42");
        capture.SetTags.Should().Contain("entitykind:store:items");
        http.Features.Get<ICacheOrchestratorFeature>()?.PendingEntityFootprint.Should().BeOfType<EntityFootprint>();
    }

    [Fact]
    public async Task GetOrSetEntityAsync_WithEntityCache_AddsDependsOnTags()
    {
        var http = new DefaultHttpContext();
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "products", ResourceId = "42" });
        DomainCacheOptions cfg = CreateConfig(domain: "store");
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, Arg.Any<HttpContext>()).Returns("entity-key");

        FootprintCapture capture = StubFootprintGetOrSet();

        string? result = await _sut.GetOrSetEntityAsync(
            http,
            _ => Task.FromResult(EntityCache.Create("dto").DependsOn("categories", "7")),
            TestContext.Current.CancellationToken);

        result.Should().Be("dto");
        capture.SetTags.Should().Contain("entity:store:products:42");
        capture.SetTags.Should().Contain("entity:store:categories:7");
    }

    [Fact]
    public async Task GetOrSetEntityAsync_WhenIdentityMissing_Throws()
    {
        var http = new DefaultHttpContext();
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig());

        var act = () => _sut.GetOrSetEntityAsync(
            http,
            _ => Task.FromResult<string?>("x"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetOrSetEntitySetAsync_UsesUrlKeyAndMemberTags()
    {
        var http = new DefaultHttpContext();
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "products", ResourceId = "should-not-shape-key" });
        DomainCacheOptions cfg = CreateConfig(domain: "store");
        _domainConfig.GetDomainOptions(http).Returns(cfg);

        bool sawResourceId = false;
        _keyGenerator
            .Generate(cfg, Arg.Any<HttpContext>())
            .Returns(ci =>
            {
                HttpContext ctx = ci.Arg<HttpContext>();
                sawResourceId = ctx.Features.Get<ICacheOrchestratorFeature>()?.ResourceId != null;
                return "url-key";
            });

        FootprintCapture capture = StubFootprintGetOrSetForList();

        IReadOnlyList<string> result = await _sut.GetOrSetEntitySetAsync(
            http,
            _ => Task.FromResult(EntitySet.Create(["a", "b"], x => x).DependsOn("categories", "9")),
            TestContext.Current.CancellationToken);

        result.Should().Equal("a", "b");
        sawResourceId.Should().BeFalse();
        (http.Features.Get<ICacheOrchestratorFeature>()?.ResourceId).Should().NotBeNull();
        capture.SetTags.Should().Contain("entity:store:products:a");
        capture.SetTags.Should().Contain("entity:store:products:b");
        capture.SetTags.Should().Contain("entity:store:categories:9");
    }

    [Fact]
    public void SetEntityIdentity_StampsRequestItems()
    {
        var http = new DefaultHttpContext();
        _sut.SetEntityIdentity(http, "Products", " 42 ");
        http.Features.Get<ICacheOrchestratorFeature>()?.EntityKind.Should().Be("products");
        http.Features.Get<ICacheOrchestratorFeature>()?.ResourceId.Should().Be("42");
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
                Arg.Do<IEnumerable<string>?>(t => capture.Tags = t?.ToArray()),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(returnValue));

        return capture;
    }

    private sealed class EntryOptionsCapture
    {
        public FusionCacheEntryOptions? Options { get; set; }
        public string[]? Tags { get; set; }
    }

    private sealed class FootprintCapture
    {
        public string[] SetTags { get; set; } = [];
    }

    private FootprintCapture StubFootprintGetOrSet()
    {
        FootprintCapture capture = new();
        _fusionCache
            .GetOrSetAsync(
                Arg.Any<string>(),
                Arg.Any<Func<FusionCacheFactoryExecutionContext<FootprintCacheBox<string?>>, CancellationToken, Task<FootprintCacheBox<string?>>>>(),
                Arg.Any<MaybeValue<FootprintCacheBox<string?>>>(),
                Arg.Any<FusionCacheEntryOptions>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Func<FusionCacheFactoryExecutionContext<FootprintCacheBox<string?>>, CancellationToken, Task<FootprintCacheBox<string?>>> factory =
                    callInfo.ArgAt<Func<FusionCacheFactoryExecutionContext<FootprintCacheBox<string?>>, CancellationToken, Task<FootprintCacheBox<string?>>>>(1);
                FootprintCacheBox<string?> box = factory(null!, CancellationToken.None).GetAwaiter().GetResult();
                return ValueTask.FromResult(box);
            });

        _fusionCache
            .SetAsync(
                Arg.Any<string>(),
                Arg.Any<FootprintCacheBox<string?>>(),
                Arg.Any<FusionCacheEntryOptions?>(),
                Arg.Do<IEnumerable<string>?>(t => capture.SetTags = t?.ToArray() ?? []),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        return capture;
    }

    private FootprintCapture StubFootprintGetOrSetForList()
    {
        FootprintCapture capture = new();
        _fusionCache
            .GetOrSetAsync(
                Arg.Any<string>(),
                Arg.Any<Func<FusionCacheFactoryExecutionContext<FootprintCacheBox<IReadOnlyList<string>?>>, CancellationToken, Task<FootprintCacheBox<IReadOnlyList<string>?>>>>(),
                Arg.Any<MaybeValue<FootprintCacheBox<IReadOnlyList<string>?>>>(),
                Arg.Any<FusionCacheEntryOptions>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Func<FusionCacheFactoryExecutionContext<FootprintCacheBox<IReadOnlyList<string>?>>, CancellationToken, Task<FootprintCacheBox<IReadOnlyList<string>?>>> factory =
                    callInfo.ArgAt<Func<FusionCacheFactoryExecutionContext<FootprintCacheBox<IReadOnlyList<string>?>>, CancellationToken, Task<FootprintCacheBox<IReadOnlyList<string>?>>>>(1);
                FootprintCacheBox<IReadOnlyList<string>?> box = factory(null!, CancellationToken.None).GetAwaiter().GetResult();
                return ValueTask.FromResult(box);
            });

        _fusionCache
            .SetAsync(
                Arg.Any<string>(),
                Arg.Any<FootprintCacheBox<IReadOnlyList<string>?>>(),
                Arg.Any<FusionCacheEntryOptions?>(),
                Arg.Do<IEnumerable<string>?>(t => capture.SetTags = t?.ToArray() ?? []),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        return capture;
    }

    // =========================
    // Helpers
    // =========================

    private static DomainCacheOptions CreateConfig(
        string domain = "products",
        bool enabled = true,
        bool respectNoStore = false,
        bool fusionRespectAuthBypass = true,
        TimeSpan? failSafe = null) => new()
        {
            Domain = domain,
            FusionCacheEnabled = enabled,
            FusionCacheRespectNoStore = respectNoStore,
            FusionRespectAuthBypass = fusionRespectAuthBypass,
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