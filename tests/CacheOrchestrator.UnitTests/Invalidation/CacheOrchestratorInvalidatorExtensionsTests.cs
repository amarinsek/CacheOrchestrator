using CacheOrchestrator.Invalidation;
using NSubstitute;

namespace CacheOrchestrator.UnitTests.Invalidation;

public class CacheOrchestratorInvalidatorExtensionsTests
{
    [Fact]
    public async Task InvalidateEntityAsync_WithInt_FormatsUsingInvariantCulture()
    {
        // Arrange
        var invalidator = Substitute.For<ICacheOrchestratorInvalidator>();
        int id = 42;
        var token = new CancellationToken();

        // Act
        await invalidator.InvalidateEntityAsync("store", "products", id, token);

        // Assert
        await invalidator.Received(1).InvalidateEntityAsync("store", "products", "42", token);
    }

    [Fact]
    public async Task InvalidateEntityAsync_WithGuid_FormatsCorrectly()
    {
        // Arrange
        var invalidator = Substitute.For<ICacheOrchestratorInvalidator>();
        Guid id = new Guid("11111111-1111-1111-1111-111111111111");

        // Act
        await invalidator.InvalidateEntityAsync("store", "users", id);

        // Assert
        await invalidator.Received(1).InvalidateEntityAsync("store", "users", "11111111-1111-1111-1111-111111111111", default);
    }

    [Fact]
    public async Task InvalidateEntitiesAsync_WithInts_FormatsAllUsingInvariantCulture()
    {
        // Arrange
        var invalidator = Substitute.For<ICacheOrchestratorInvalidator>();
        int[] ids = [1, 2, 3];

        // Act
        await invalidator.InvalidateEntitiesAsync("store", "products", ids);

        // Assert
        await invalidator.Received(1).InvalidateEntitiesAsync(
            "store",
            "products",
            Arg.Is<IEnumerable<string>>(list => list.SequenceEqual(new[] { "1", "2", "3" })),
            default);
    }
}
