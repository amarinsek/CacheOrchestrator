using CacheOrchestrator.Orchestration;

namespace CacheOrchestrator.Core.UnitTests.Orchestration;

public class CacheDomainContextTests
{
    [Fact]
    public void Constructor_normalizes_domain_and_entity_kind()
    {
        CacheDomainContext ctx = new("Catalog", "Products");
        Assert.Equal("catalog", ctx.Domain);
        Assert.Equal("products", ctx.EntityKind);
    }

    [Fact]
    public void EntityKindOr_uses_default_when_unset()
    {
        CacheDomainContext ctx = new("catalog");
        Assert.Null(ctx.EntityKind);
        Assert.Equal("products", ctx.EntityKindOr("Products"));
    }

    [Fact]
    public void Constructor_rejects_empty_domain()
    {
        Assert.Throws<ArgumentException>(() => new CacheDomainContext("  "));
    }

    [Fact]
    public async Task GetOrCreateEntityAsync_formats_generic_id_with_invariant_culture()
    {
        ICacheOrchestrator cache = Substitute.For<ICacheOrchestrator>();
        CacheDomainContext context = new("catalog", "products");
        Func<CancellationToken, ValueTask<string?>> factory =
            _ => ValueTask.FromResult<string?>("value");
        CacheEntryRequest? captured = null;
        cache.GetOrCreateWithFootprintAsync<string>(
                Arg.Any<CacheEntryRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<FootprintCacheBox<string?>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<CacheEntryRequest>(0);
                return callInfo
                    .ArgAt<Func<CancellationToken, ValueTask<FootprintCacheBox<string?>>>>(1)
                    .Invoke(callInfo.ArgAt<CancellationToken>(2));
            });

        string? result = await cache.GetOrCreateEntityAsync(
            context,
            "product:42",
            42,
            factory,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be("value");
        captured.Should().NotBeNull();
        captured!.Domain.Should().Be("catalog");
        captured.Key.Should().Be("product:42");
        captured.Footprint!.Primary.Should().Be(
            new CacheOrchestrator.Entity.EntityRef("products", "42"));
    }

    [Fact]
    public async Task GetOrCreateEntityAsync_with_footprint_factory_accepts_generic_id()
    {
        ICacheOrchestrator cache = Substitute.For<ICacheOrchestrator>();
        CacheDomainContext context = new("catalog", "products");
        Func<CancellationToken, ValueTask<CacheOrchestrator.Entity.EntityCache<string>>> factory =
            _ => ValueTask.FromResult(CacheOrchestrator.Entity.EntityCache.Create("value"));
        CacheEntryRequest? captured = null;
        cache.GetOrCreateWithFootprintAsync<string>(
                Arg.Any<CacheEntryRequest>(),
                Arg.Any<Func<CancellationToken, ValueTask<FootprintCacheBox<string?>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<CacheEntryRequest>(0);
                return callInfo
                    .ArgAt<Func<CancellationToken, ValueTask<FootprintCacheBox<string?>>>>(1)
                    .Invoke(callInfo.ArgAt<CancellationToken>(2));
            });

        string? result = await cache.GetOrCreateEntityAsync(
            context,
            "product:42",
            42,
            factory,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be("value");
        captured!.Footprint!.Primary.Should().Be(
            new CacheOrchestrator.Entity.EntityRef("products", "42"));
    }
}
