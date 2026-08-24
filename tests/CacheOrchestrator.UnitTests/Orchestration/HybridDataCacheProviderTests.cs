using CacheOrchestrator.Configuration;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using MsHybrid = Microsoft.Extensions.Caching.Hybrid;

namespace CacheOrchestrator.UnitTests.Orchestration;

public class HybridDataCacheProviderTests
{
    private readonly MsHybrid.HybridCache _cache = Substitute.For<MsHybrid.HybridCache>();
    private readonly CacheOrchestrator.HybridCache.HybridDataCacheProvider _sut;

    public HybridDataCacheProviderTests()
    {
        _sut = new CacheOrchestrator.HybridCache.HybridDataCacheProvider(
            _cache,
            NullLogger<CacheOrchestrator.HybridCache.HybridDataCacheProvider>.Instance);
    }

    [Fact]
    public void Name_IsHybridCache() => _sut.Name.Should().Be("HybridCache");

    [Fact]
    public async Task GetOrCreateAsync_CallsHybridWithKeyTagsAndDataCacheTtl()
    {
        string? passedKey = null;
        MsHybrid.HybridCacheEntryOptions? passedOptions = null;
        IEnumerable<string>? passedTags = null;

        _cache
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, ValueTask<string>>>(),
                Arg.Any<Func<Func<CancellationToken, ValueTask<string>>, CancellationToken, ValueTask<string>>>(),
                Arg.Any<MsHybrid.HybridCacheEntryOptions?>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                passedKey = callInfo.ArgAt<string>(0);
                passedOptions = callInfo.ArgAt<MsHybrid.HybridCacheEntryOptions?>(3);
                passedTags = callInfo.ArgAt<IEnumerable<string>?>(4);
                return ValueTask.FromResult("hit");
            });

        DomainCacheOptions domain = new()
        {
            Domain = "products",
            VersionHex = "v",
            DataCacheTtl = TimeSpan.FromMinutes(5),
            FusionCacheInstanceName = "default",
        };

        DataCacheProviderRequest request = new()
        {
            Key = "products:v:product:1",
            InstanceName = "default",
            Tags = ["domain:products", "entity:products:product:1"],
            DomainOptions = domain,
        };

        string result = await _sut.GetOrCreateAsync(
            request,
            _ => ValueTask.FromResult("fresh"),
            TestContext.Current.CancellationToken);

        result.Should().Be("hit");
        passedKey.Should().Be("products:v:product:1");
        passedOptions!.Expiration.Should().Be(TimeSpan.FromMinutes(5));
        passedOptions.LocalCacheExpiration.Should().Be(TimeSpan.FromMinutes(5));
        passedTags.Should().Equal("domain:products", "entity:products:product:1");
    }

    [Fact]
    public async Task RemoveByTagAsync_DelegatesToHybrid()
    {
        await _sut.RemoveByTagAsync("domain:products", TestContext.Current.CancellationToken);

        await _cache.Received(1).RemoveByTagAsync("domain:products", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveByTagAsync_WithInstanceName_StillUsesSingleHybridCache()
    {
        await _sut.RemoveByTagAsync("secondary", "domain:products", TestContext.Current.CancellationToken);

        await _cache.Received(1).RemoveByTagAsync("domain:products", Arg.Any<CancellationToken>());
    }
}
