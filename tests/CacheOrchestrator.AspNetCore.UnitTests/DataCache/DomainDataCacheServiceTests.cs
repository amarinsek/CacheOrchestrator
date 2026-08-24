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
        _keyGenerator.Generate(cfg, http).Returns("reports:v:key");
        StubOrchestratorGetOrCreate(1);

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
        StubOrchestratorGetOrCreate(1);

        await _sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        _domainConfig.Received(1).EnsureDomainOptions(http, "catalog");
    }

    [Fact]
    public async Task GetOrSetAsync_WhenDataCacheDisabled_CallsFactoryDirectly()
    {
        var http = new DefaultHttpContext();
        DomainCacheOptions cfg = CreateConfig(enabled: false);
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
        DomainCacheOptions cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        _keyGenerator.Generate(cfg, http).Returns("products:abc:hash");

        CacheEntryRequest? captured = null;
        _orchestrator
            .GetOrCreateAsync(
                Arg.Any<CacheEntryRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<CacheEntryRequest>(0);
                return ValueTask.FromResult<string?>("ok");
            });

        string result = await _sut.GetOrSetAsync(http, _ => Task.FromResult("ok"), TestContext.Current.CancellationToken);

        result.Should().Be("ok");
        captured.Should().NotBeNull();
        captured!.Key.Should().Be("products:abc:hash");
        captured.KeyIsPhysical.Should().BeTrue();
        captured.Domain.Should().Be("products");
    }

    [Fact]
    public async Task GetOrSetEntityAsync_RequiresIdentity_AndStagesFootprint()
    {
        var http = new DefaultHttpContext();
        DomainCacheOptions cfg = CreateConfig();
        _domainConfig.GetDomainOptions(http).Returns(cfg);
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature
        {
            EntityKind = "products",
            ResourceId = "42"
        });
        _keyGenerator.Generate(cfg, http).Returns("products:v:id:products:42:hash");

        _orchestrator
            .GetOrCreateWithFootprintAsync(
                Arg.Any<CacheEntryRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<FootprintCacheBox<string?>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var factory = callInfo.ArgAt<Func<CancellationToken, ValueTask<FootprintCacheBox<string?>>>>(1);
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
        var admin = Substitute.For<IAdminStatsCollector>();
        admin.IsEnabled.Returns(true);
        var sut = new DomainDataCacheService(
            _orchestrator,
            _domainConfig,
            _keyGenerator,
            NullLogger<DomainDataCacheService>.Instance,
            admin);
        var http = new DefaultHttpContext();
        DomainCacheOptions cfg = CreateConfig(enabled: false);
        _domainConfig.GetDomainOptions(http).Returns(cfg);

        await sut.GetOrSetAsync(http, _ => Task.FromResult(1), TestContext.Current.CancellationToken);

        admin.Received().RecordDataCache(
            Arg.Any<string?>(),
            "products",
            "off",
            Arg.Any<long?>(),
            Arg.Any<long?>());
    }

    private void StubOrchestratorGetOrCreate<T>(T value)
    {
        _orchestrator
            .GetOrCreateAsync(
                Arg.Any<CacheEntryRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<T?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<T?>(value));
    }

    private static DomainCacheOptions CreateConfig(string domain = "products", bool enabled = true) => new()
    {
        Domain = domain,
        Version = "1",
        VersionHex = "abc",
        DataCacheEnabled = enabled,
        DataCacheInstanceName = "default",
        DataCacheTtl = TimeSpan.FromMinutes(5),
    };
}
