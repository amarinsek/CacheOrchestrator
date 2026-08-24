using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.FusionCache.UnitTests.Orchestration;

public class FusionDataCacheProviderTests
{
    private readonly IFusionCacheProvider _fusionProvider = Substitute.For<IFusionCacheProvider>();
    private readonly IFusionCache _fusionCache = Substitute.For<IFusionCache>();
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options =
        Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
    private readonly FusionDataCacheProvider _sut;

    public FusionDataCacheProviderTests()
    {
        _fusionProvider.GetCache("default").Returns(_fusionCache);
        _options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            DataCacheInstances =
            {
                ["default"] = new CacheOrchestratorOptions.DataCacheInstanceOptions()
            }
        });
        _sut = new FusionDataCacheProvider(
            _fusionProvider,
            _options,
            NullLogger<FusionDataCacheProvider>.Instance);
    }

    [Fact]
    public void Name_IsFusionCache() => _sut.Name.Should().Be("FusionCache");

    [Fact]
    public async Task GetOrCreateAsync_CallsFusionGetOrSetWithKeyTagsAndFactory()
    {
        DomainCacheOptions domain = new()
        {
            Domain = "products",
            VersionHex = "v",
            DataCacheInstanceName = "default",
            DataCacheTtl = TimeSpan.FromMinutes(1)
        };

        string? passedKey = null;
        IEnumerable<string>? passedTags = null;
        _fusionCache
            .GetOrSetAsync(
                Arg.Any<string>(),
                Arg.Any<Func<FusionCacheFactoryExecutionContext<string>, CancellationToken, Task<string>>>(),
                Arg.Any<MaybeValue<string>>(),
                Arg.Any<FusionCacheEntryOptions>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                passedKey = callInfo.ArgAt<string>(0);
                passedTags = callInfo.ArgAt<IEnumerable<string>?>(4);
                return ValueTask.FromResult("hit");
            });

        DataCacheProviderRequest request = new()
        {
            Key = "products:v:product:1",
            InstanceName = "default",
            Tags = ["domain:products", "entity:products:product:1"],
            DomainOptions = domain
        };

        string result = await _sut.GetOrCreateAsync(
            request,
            _ => ValueTask.FromResult("fresh"),
            TestContext.Current.CancellationToken);

        result.Should().Be("hit");
        passedKey.Should().Be("products:v:product:1");
        passedTags.Should().Equal("domain:products", "entity:products:product:1");
    }

    [Fact]
    public async Task RemoveByTagAsync_RemovesOnAllConfiguredInstances()
    {
        IFusionCache second = Substitute.For<IFusionCache>();
        _fusionProvider.GetCache("secondary").Returns(second);
        _options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            DataCacheInstances =
            {
                ["default"] = new CacheOrchestratorOptions.DataCacheInstanceOptions(),
                ["secondary"] = new CacheOrchestratorOptions.DataCacheInstanceOptions()
            }
        });

        await _sut.RemoveByTagAsync("domain:products", TestContext.Current.CancellationToken);

        await _fusionCache.Received(1).RemoveByTagAsync("domain:products", token: Arg.Any<CancellationToken>());
        await second.Received(1).RemoveByTagAsync("domain:products", token: Arg.Any<CancellationToken>());
    }
}
