using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CacheOrchestrator.UnitTests.Orchestration;

public class CacheOrchestratorServiceTests
{
    private readonly IDomainCacheOptionsProvider _domainOptions = Substitute.For<IDomainCacheOptionsProvider>();
    private readonly IDataCacheProvider _dataCache = Substitute.For<IDataCacheProvider>();
    private readonly CacheOrchestratorService _sut;

    public CacheOrchestratorServiceTests()
    {
        _dataCache.Name.Returns("FusionCache");
        _sut = new CacheOrchestratorService(
            _domainOptions,
            _dataCache,
            NullLogger<CacheOrchestratorService>.Instance);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenDataCacheDisabled_RunsFactoryUncached()
    {
        DomainCacheOptions opts = CreateOptions(enabled: false);
        _domainOptions.GetOrCreateDomainOptions("products").Returns(opts);

        bool factoryCalled = false;
        string? value = await _sut.GetOrCreateAsync(
            new CacheEntryRequest { Domain = "products", Key = "product:1" },
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult<string?>("fresh");
            },
            TestContext.Current.CancellationToken);

        value.Should().Be("fresh");
        factoryCalled.Should().BeTrue();
        await _dataCache.DidNotReceive()
            .GetOrCreateAsync(
                Arg.Any<DataCacheProviderRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenEnabled_DelegatesToProviderWithPhysicalKeyAndDomainTag()
    {
        DomainCacheOptions opts = CreateOptions(enabled: true, domain: "products", versionHex: "abc123");
        _domainOptions.GetOrCreateDomainOptions("products").Returns(opts);

        DataCacheProviderRequest? captured = null;
        _dataCache
            .GetOrCreateAsync(
                Arg.Any<DataCacheProviderRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<DataCacheProviderRequest>();
                return ValueTask.FromResult<string?>("cached");
            });

        string? value = await _sut.GetOrCreateAsync(
            new CacheEntryRequest { Domain = "products", Key = "product:42" },
            _ => ValueTask.FromResult<string?>("fresh"),
            TestContext.Current.CancellationToken);

        value.Should().Be("cached");
        captured.Should().NotBeNull();
        captured!.Key.Should().Be("products:abc123:product:42");
        captured.InstanceName.Should().Be("default");
        captured.Tags.Should().Equal("domain:products");
        captured.DomainOptions.Should().BeSameAs(opts);
    }

    [Fact]
    public async Task GetOrCreateAsync_WithFootprint_ExpandsEntityTags()
    {
        DomainCacheOptions opts = CreateOptions(enabled: true, domain: "store", versionHex: "v1");
        _domainOptions.GetOrCreateDomainOptions("store").Returns(opts);

        DataCacheProviderRequest? captured = null;
        _dataCache
            .GetOrCreateAsync(
                Arg.Any<DataCacheProviderRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<int?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<DataCacheProviderRequest>();
                return ValueTask.FromResult<int?>(1);
            });

        EntityFootprint footprint = new(new EntityRef("items", "42"));

        await _sut.GetOrCreateAsync(
            new CacheEntryRequest
            {
                Domain = "store",
                Key = "id:items:42",
                Footprint = footprint
            },
            _ => ValueTask.FromResult<int?>(1),
            TestContext.Current.CancellationToken);

        captured!.Tags.Should().Equal(
            "domain:store",
            "entity:store:items:42",
            "entitykind:store:items");
    }

    [Fact]
    public async Task GetOrCreateAsync_WithAdditionalTags_MergesWithoutDuplicates()
    {
        DomainCacheOptions opts = CreateOptions(enabled: true, domain: "store", versionHex: "v1");
        _domainOptions.GetOrCreateDomainOptions("store").Returns(opts);

        DataCacheProviderRequest? captured = null;
        _dataCache
            .GetOrCreateAsync(
                Arg.Any<DataCacheProviderRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<int?>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<DataCacheProviderRequest>();
                return ValueTask.FromResult<int?>(1);
            });

        await _sut.GetOrCreateAsync(
            new CacheEntryRequest
            {
                Domain = "store",
                Key = "list",
                AdditionalTags = ["domain:store", "custom:tag"]
            },
            _ => ValueTask.FromResult<int?>(1),
            TestContext.Current.CancellationToken);

        captured!.Tags.Should().Equal("domain:store", "custom:tag");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenDomainMissing_Throws()
    {
        Func<Task> act = () => _sut.GetOrCreateAsync(
            new CacheEntryRequest { Domain = "  ", Key = "k" },
            _ => ValueTask.FromResult<string?>("x"),
            TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenKeyMissing_Throws()
    {
        Func<Task> act = () => _sut.GetOrCreateAsync(
            new CacheEntryRequest { Domain = "products", Key = "" },
            _ => ValueTask.FromResult<string?>("x"),
            TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void BuildPhysicalKey_IncludesDomainVersionAndLogicalKey()
    {
        DomainCacheOptions opts = CreateOptions(domain: "catalog", versionHex: "deadbeef");
        CacheOrchestratorService.BuildPhysicalKey(opts, "page:1")
            .Should().Be("catalog:deadbeef:page:1");
    }

    private static DomainCacheOptions CreateOptions(
        bool enabled = true,
        string domain = "products",
        string versionHex = "1")
        => new()
        {
            Domain = domain,
            Version = "1",
            VersionHex = versionHex,
            FusionCacheEnabled = enabled,
            FusionCacheInstanceName = "default",
            FusionCacheSoftTtl = TimeSpan.FromMinutes(5),
            FusionCacheHardTtl = TimeSpan.FromHours(1),
            FusionCacheFailSafe = TimeSpan.FromHours(2)
        };
}
