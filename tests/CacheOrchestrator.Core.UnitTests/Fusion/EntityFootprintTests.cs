using CacheOrchestrator.Entity;

namespace CacheOrchestrator.Core.UnitTests.Fusion;

public class EntityFootprintTests
{
    [Fact]
    public void ToTags_IncludesDomainPrimaryMembersDependsOnAndAliases()
    {
        var footprint = new EntityFootprint(
            primary: new EntityRef("orders", "5"),
            members: [new EntityRef("order-lines", "100")],
            dependsOn: [new EntityRef("customers", "9"), new EntityRef("products", "42")],
            aliases: [new EntityRef("orders-by-number", "A-5")]);

        IReadOnlyList<string> tags = footprint.ToTags("store");

        tags.Should().Contain("domain:store");
        tags.Should().Contain("entity:store:orders:5");
        tags.Should().Contain("entitykind:store:orders");
        tags.Should().Contain("entity:store:order-lines:100");
        tags.Should().Contain("entitykind:store:order-lines");
        tags.Should().Contain("entity:store:customers:9");
        tags.Should().Contain("entity:store:products:42");
        tags.Should().Contain("entity:store:orders-by-number:A-5");
    }

    [Fact]
    public void ToTags_DedupesRepeatedRefs()
    {
        var footprint = new EntityFootprint(
            primary: new EntityRef("products", "1"),
            members: [new EntityRef("products", "1")],
            dependsOn: [new EntityRef("products", "1")]);

        footprint.ToTags("store").Should().Equal(
            "domain:store",
            "entity:store:products:1",
            "entitykind:store:products");
    }

    [Fact]
    public void WithPrimary_WhenIdentityIsUnchanged_ReusesFootprint()
    {
        var footprint = new EntityFootprint(new EntityRef("products", "1"));

        footprint.WithPrimary(new EntityRef("products", "1")).Should().BeSameAs(footprint);
    }

    [Fact]
    public void Merge_WhenInstanceIsUnchanged_ReusesFootprint()
    {
        var footprint = new EntityFootprint(new EntityRef("products", "1"));

        footprint.Merge(footprint).Should().BeSameAs(footprint);
    }

    [Fact]
    public void EntityCache_DependsOn_ExtendsFootprint()
    {
        EntityCache<string> cache = EntityCache.Create("x")
            .DependsOn("categories", "7")
            .Alias("products-by-sku", "SKU-1");

        cache.Value.Should().Be("x");
        cache.IsMiss.Should().BeFalse();
        cache.Footprint.DependsOn.Should().ContainSingle(r => r.EntityKind == "categories" && r.ResourceId == "7");
        cache.Footprint.Aliases.Should().ContainSingle(r => r.EntityKind == "products-by-sku");
    }

    [Fact]
    public void EntitySet_BuildFootprint_UsesDefaultMemberKind()
    {
        Item[] items = [new("1"), new("2")];
        EntitySet<Item> set = EntitySet.Create(items, i => i.Id)
            .DependsOn("categories", "9");

        EntityFootprint footprint = set.BuildFootprint("products");
        footprint.Members.Should().HaveCount(2);
        footprint.Members[0].EntityKind.Should().Be("products");
        footprint.DependsOn.Should().ContainSingle(r => r.ResourceId == "9");
    }

    private sealed record Item(string Id);
}
