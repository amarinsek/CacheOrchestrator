using CacheOrchestrator.Entity;

namespace CacheOrchestrator.Core.UnitTests.Fusion;

public class EntityCacheTests
{
    [Fact]
    public void DependsOn_WithGenericId_FormatsCorrectly()
    {
        // Arrange
        var cache = EntityCache.Create("val");
        int id = 42;

        // Act
        EntityCache<string> updated = cache.DependsOn("category", id);

        // Assert
        Assert.Contains(updated.Footprint.DependsOn, r => r.EntityKind == "category" && r.ResourceId == "42");
    }

    [Fact]
    public void Members_WithGenericIds_FormatsCorrectly()
    {
        // Arrange
        var cache = EntityCache.Create("val");
        Guid[] ids = [Guid.Parse("11111111-1111-1111-1111-111111111111")];

        // Act
        EntityCache<string> updated = cache.Members("user", ids);

        // Assert
        Assert.Contains(updated.Footprint.Members, r => r.EntityKind == "user" && r.ResourceId == "11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Alias_WithGenericId_FormatsCorrectly()
    {
        // Arrange
        var cache = EntityCache.Create("val");
        long id = 123456789;

        // Act
        EntityCache<string> updated = cache.Alias("sku", id);

        // Assert
        Assert.Contains(updated.Footprint.Aliases, r => r.EntityKind == "sku" && r.ResourceId == "123456789");
    }
}
