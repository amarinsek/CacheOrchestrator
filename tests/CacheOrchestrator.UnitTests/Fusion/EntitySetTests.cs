using CacheOrchestrator.FusionCache;

namespace CacheOrchestrator.UnitTests.Fusion;

public class EntitySetTests
{
    private record Product(int Id, string Category);

    [Fact]
    public void Create_WithGenericIdSelector_FormatsCorrectly()
    {
        // Arrange
        Product[] products = [new(42, "toys")];

        // Act
        var set = EntitySet.Create(products, "products", p => p.Id);
        var footprint = set.BuildFootprint("products");

        // Assert
        Assert.Contains(footprint.Members, r => r.EntityKind == "products" && r.ResourceId == "42");
    }

    [Fact]
    public void DependsOn_WithGenericId_FormatsCorrectly()
    {
        // Arrange
        var set = EntitySet.Create(Array.Empty<Product>(), "products", p => p.Id);
        int categoryId = 99;

        // Act
        var updated = set.DependsOn("category", categoryId);
        var footprint = updated.BuildFootprint("products");

        // Assert
        Assert.Contains(footprint.DependsOn, r => r.EntityKind == "category" && r.ResourceId == "99");
    }

    [Fact]
    public void DependsOn_WithGenericIdSelector_FormatsCorrectly()
    {
        // Arrange
        Product[] products = [new(42, "toys")];
        var set = EntitySet.Create(products, "products", p => p.Id);

        // Act
        var updated = set.DependsOn(p => "category", p => p.Id * 10);
        var footprint = updated.BuildFootprint("products");

        // Assert
        Assert.Contains(footprint.DependsOn, r => r.EntityKind == "category" && r.ResourceId == "420");
    }
}
