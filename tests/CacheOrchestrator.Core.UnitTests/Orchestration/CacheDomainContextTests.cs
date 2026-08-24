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
}
