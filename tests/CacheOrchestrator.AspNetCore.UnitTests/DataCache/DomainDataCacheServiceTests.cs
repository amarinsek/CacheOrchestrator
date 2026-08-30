using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.Entity;
using CacheOrchestrator.Orchestration;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CacheOrchestrator.AspNetCore.UnitTests.DataCache;

public class DomainDataCacheServiceTests
{
    private readonly ICacheOrchestrator _orchestrator = Substitute.For<ICacheOrchestrator>();
    private readonly IRequestDomainCacheOptions _domainConfig = Substitute.For<IRequestDomainCacheOptions>();
    private readonly IDomainKeyGenerator _keyGenerator = Substitute.For<IDomainKeyGenerator>();
    private readonly DomainDataCacheService _sut;

    public DomainDataCacheServiceTests()
    {
        _sut = new DomainDataCacheService(
            _orchestrator,
            _domainConfig,
            _keyGenerator,
            NullLogger<DomainDataCacheService>.Instance);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenConfigIsNull_AndNoEndpointDomain_CallsFactoryDirectly()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Path = "/api/uncached";
        _domainConfig.GetDomainOptions(http).Returns((DomainHttpCacheOptions?)null);

        bool factoryCalled = false;
        int result = await _sut.GetOrSetAsync(http, _ =>
        {
            factoryCalled = true;
            return Task.FromResult(42);
        }, TestContext.Current.CancellationToken);

        result.Should().Be(42);
        factoryCalled.Should().BeTrue();
        _domainConfig.DidNotReceive().EnsureDomainOptions(Arg.Any<HttpContext>(), Arg.Any<string>());
        http.Features.Get<ICacheOrchestratorFeature>()?.Disposition.Should().BeOfType<CacheDisposition>()
            .Which.Data.Should().Be(DataCacheResult.Unresolved);
    }

    [Fact]
    public async Task GetOrSetAsync_WithDomainOverload_WhenConfigMissing_EnsuresDomain()
    {
        var http = new DefaultHttpContext();
        DomainHttpCacheOptions cfg = CreateConfig(domain: "reports");
        _domainConfig.GetDomainOptions(http).Returns((DomainHttpCacheOptions?)null);
        _domainConfig.EnsureDomainOptions(http, "reports").Returns(cfg);
        _keyGenerator.Generate(Arg.Is<DomainCacheKeyContext>(context =>
            context.Options == cfg && context.HttpContext == http && context.Shape == DomainCacheKeyShape.Url))
            .Returns("reports:v:key");
        StubOrchestratorGetOrCreate(1);

        await _sut.GetOrSetAsync(http, "reports", _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        _domainConfig.Received(1).EnsureDomainOptions(http, "reports");
        _keyGenerator.Received(1).Generate(Arg.Is<DomainCacheKeyContext>(context =>
            context.Options == cfg && context.HttpContext == http && context.Shape == DomainCacheKeyShape.Url));
    }

    [Fact]
    public async Task GetOrSetAsync_WhenConfigMissing_ResolvesDomainFromPolicyMetadata()
    {
        var http = new DefaultHttpContext();
        DomainHttpCacheOptions cfg = CreateConfig(domain: "catalog");
        _domainConfig.GetDomainOptions(http).Returns((DomainHttpCacheOptions?)null);
        _domainConfig.EnsureDomainOptions(http, "catalog").Returns(cfg);
        StubKey(cfg, http, DomainCacheKeyShape.Url, "key");

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new DomainOutputCachePolicy("catalog")),
            "test");
        http.SetEndpoint(endpoint);
        StubOrchestratorGetOrCreate(1);

        await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        _domainConfig.Received(1).EnsureDomainOptions(http, "catalog");
    }

    [Fact]
    public async Task GetOrSetAsync_WhenDataCacheDisabled_CallsFactoryDirectly()
    {
        var http = new DefaultHttpContext();
        DomainHttpCacheOptions cfg = CreateConfig(enabled: false);
        _domainConfig.GetDomainOptions(http).Returns(cfg);

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
        await _orchestrator.DidNotReceive()
            .GetOrCreateAsync(Arg.Any<CacheEntryRequest>(), Arg.Any<Func<CancellationToken, ValueTask<int?>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrSetAsync_WhenEnabled_DelegatesToOrchestratorWithPhysicalKey()
    {
        var http = new DefaultHttpContext();
        DomainHttpCacheOptions cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        StubKey(cfg, http, DomainCacheKeyShape.Url, "co3:products:abc:u:hash");

        CacheEntryRequest? captured = null;
        _orchestrator
            .GetOrCreateAsync(
                Arg.Any<CacheEntryRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<CacheEntryRequest>(0);
                captured.OutcomeObserver?.Invoke(DataCacheProviderOutcome.Cached);
                return ValueTask.FromResult<string?>("ok");
            });

        string result = await _sut.GetOrSetAsync(http, _ => Task.FromResult("ok"), TestContext.Current.CancellationToken);

        result.Should().Be("ok");
        captured.Should().NotBeNull();
        captured.Key.Should().Be("co3:products:abc:u:hash");
        captured.KeyIsPhysical.Should().BeTrue();
        captured.Domain.Should().Be("products");
    }

    [Fact]
    public async Task GetOrSetEntityAsync_RequiresIdentity_AndStagesFootprint()
    {
        var http = new DefaultHttpContext();
        DomainHttpCacheOptions cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature
        {
            EntityKind = "products",
            ResourceId = "42"
        });
        StubKey(cfg, http, DomainCacheKeyShape.Entity, "co3:products:v:e:hash");

        _orchestrator
            .GetOrCreateWithFootprintAsync(
                Arg.Any<CacheEntryRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<FootprintCacheBox<string?>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                CacheEntryRequest request = callInfo.ArgAt<CacheEntryRequest>(0);
                Func<CancellationToken, ValueTask<FootprintCacheBox<string?>>> factory = callInfo.ArgAt<Func<CancellationToken, ValueTask<FootprintCacheBox<string?>>>>(1);
                request.OutcomeObserver?.Invoke(DataCacheProviderOutcome.Materialized);
                return factory(CancellationToken.None);
            });

        string? value = await _sut.GetOrSetEntityAsync(
            http,
            _ => Task.FromResult<string?>("sku"),
            TestContext.Current.CancellationToken);

        value.Should().Be("sku");
        http.Features.Get<ICacheOrchestratorFeature>()!.PendingEntityFootprint.Should().NotBeNull();
        http.Features.Get<ICacheOrchestratorFeature>()!.PendingEntityFootprint!.Primary.Should().NotBeNull();
    }

    [Fact]
    public void SetEntityIdentity_NormalizesOnFeature()
    {
        var http = new DefaultHttpContext();
        _sut.SetEntityIdentity(http, "Products", " 99 ");
        ICacheOrchestratorFeature feature = http.Features.Get<ICacheOrchestratorFeature>()!;
        feature.EntityKind.Should().Be("products");
        feature.ResourceId.Should().Be("99");
    }

    [Fact]
    public async Task GetOrSetEntityAsync_WhenIdentityMissing_Throws()
    {
        var http = new DefaultHttpContext();
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig());

        Func<Task> act = () => _sut.GetOrSetEntityAsync(
            http,
            _ => Task.FromResult<string?>("x"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Entity identity*");
    }

    [Fact]
    public async Task GetOrSetAsync_WhenDisabled_RecordsFactoryRunOnAdmin()
    {
        IAdminStatsCollector admin = Substitute.For<IAdminStatsCollector>();
        admin.IsEnabled.Returns(true);
        var sut = new DomainDataCacheService(
            _orchestrator,
            _domainConfig,
            _keyGenerator,
            NullLogger<DomainDataCacheService>.Instance,
            admin);
        var http = new DefaultHttpContext();
        DomainHttpCacheOptions cfg = CreateConfig(enabled: false);
        _domainConfig.GetDomainOptions(http).Returns(cfg);

        await sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        admin.Received().RecordDataCache(
            Arg.Any<string?>(),
            "products",
            "off",
            Arg.Any<long?>(),
            Arg.Any<long?>());
    }

    [Fact]
    public async Task GetOrSetEntityAsync_WhenDisabled_RecordsTimedFactoryRun()
    {
        IAdminStatsCollector admin = Substitute.For<IAdminStatsCollector>();
        admin.IsEnabled.Returns(true);
        admin.TrackLatency.Returns(true);
        var sut = new DomainDataCacheService(
            _orchestrator,
            _domainConfig,
            _keyGenerator,
            NullLogger<DomainDataCacheService>.Instance,
            admin);
        var http = new DefaultHttpContext();
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig(enabled: false));
        sut.SetEntityIdentity(http, "products", "42");

        await sut.GetOrSetEntityAsync(
            http,
            _ => Task.FromResult<string?>("value"),
            TestContext.Current.CancellationToken);

        admin.Received().RecordDataCache(
            Arg.Any<string?>(),
            "products",
            "off",
            Arg.Is<long?>(value => value.HasValue),
            Arg.Any<long?>());
    }

    [Fact]
    public async Task GetOrSetEntityAsync_WhenDisabledFactoryFails_RecordsFailure()
    {
        IAdminStatsCollector admin = Substitute.For<IAdminStatsCollector>();
        admin.IsEnabled.Returns(true);
        var sut = new DomainDataCacheService(
            _orchestrator,
            _domainConfig,
            _keyGenerator,
            NullLogger<DomainDataCacheService>.Instance,
            admin);
        var http = new DefaultHttpContext();
        _domainConfig.GetDomainOptions(http).Returns(CreateConfig(enabled: false));
        sut.SetEntityIdentity(http, "products", "42");

        Func<Task> act = () => sut.GetOrSetEntityAsync<string>(
            http,
            _ => Task.FromException<string?>(new InvalidOperationException("boom")),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        admin.Received().RecordDataCache(
            Arg.Any<string?>(),
            "products",
            "fail",
            Arg.Any<long?>(),
            Arg.Any<long?>());
    }

    [Fact]
    public async Task GetOrSetAsync_WhenProviderReturnsStale_ReportsStaleEvenWithoutSynchronousFactoryFailure()
    {
        var http = new DefaultHttpContext();
        DomainHttpCacheOptions cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        StubKey(cfg, http, DomainCacheKeyShape.Url, "co3:products:abc:u:key");
        _orchestrator
            .GetOrCreateAsync(
                Arg.Any<CacheEntryRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                CacheEntryRequest request = callInfo.ArgAt<CacheEntryRequest>(0);
                request.OutcomeObserver?.Invoke(DataCacheProviderOutcome.Stale);
                return ValueTask.FromResult<string?>("stale-value");
            });

        string result = await _sut.GetOrSetAsync(
            http,
            _ => Task.FromResult("fresh-value"),
            TestContext.Current.CancellationToken);

        result.Should().Be("stale-value");
        http.Features.Get<ICacheOrchestratorFeature>()!.Disposition!.Data
            .Should().Be(DataCacheResult.Stale);
    }

    private void StubOrchestratorGetOrCreate<T>(T value)
    {
        _orchestrator
            .GetOrCreateAsync(
                Arg.Any<CacheEntryRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<T?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<CacheEntryRequest>(0).OutcomeObserver?.Invoke(DataCacheProviderOutcome.Cached);
                return ValueTask.FromResult<T?>(value);
            });
    }

    private void StubKey(
        DomainHttpCacheOptions options,
        HttpContext http,
        DomainCacheKeyShape shape,
        string key)
    {
        _keyGenerator.Generate(Arg.Is<DomainCacheKeyContext>(context =>
            context.Options == options && context.HttpContext == http && context.Shape == shape))
            .Returns(key);
    }

    private static DomainHttpCacheOptions CreateConfig(string domain = "products", bool enabled = true) => new()
    {
        CoreOptions = new DomainCacheOptions
        {
            Domain = domain,
            Version = "1",
            VersionHex = "abc",
            DataCacheEnabled = enabled,
            DataCacheInstanceName = "default",
            DataCacheTtl = TimeSpan.FromMinutes(5),
        },
    };
}
