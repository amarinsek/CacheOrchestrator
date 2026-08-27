using CacheOrchestrator.Invalidation;
using NSubstitute;

namespace CacheOrchestrator.Core.UnitTests.Invalidation;

public class CacheOrchestratorInvalidatorExtensionsTests
{
    [Fact]
    public async Task InvalidateEntityAsync_WithInt_FormatsUsingInvariantCulture()
    {
        // Arrange
        ICacheOrchestratorInvalidator invalidator = Substitute.For<ICacheOrchestratorInvalidator>();
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
        ICacheOrchestratorInvalidator invalidator = Substitute.For<ICacheOrchestratorInvalidator>();
        var id = new Guid("11111111-1111-1111-1111-111111111111");

        // Act
        await invalidator.InvalidateEntityAsync("store", "users", id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await invalidator.Received(1).InvalidateEntityAsync("store", "users", "11111111-1111-1111-1111-111111111111", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InvalidateEntitiesAsync_WithInts_FormatsAllUsingInvariantCulture()
    {
        // Arrange
        ICacheOrchestratorInvalidator invalidator = Substitute.For<ICacheOrchestratorInvalidator>();
        int[] ids = [1, 2, 3];

        // Act
        await invalidator.InvalidateEntitiesAsync("store", "products", ids, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await invalidator.Received(1).InvalidateEntitiesAsync(
            "store",
            "products",
            Arg.Is<IEnumerable<string>>(list => list.SequenceEqual(new[] { "1", "2", "3" })),
            TestContext.Current.CancellationToken);
    }
}
