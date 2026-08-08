using Testcontainers.Redis;

namespace CacheOrchestrator.IntegrationTests.Infrastructure;

/// <summary>
/// Shared Redis container for integration tests marked with <c>[Collection("Redis")]</c>.
/// Requires a working Docker engine (local Desktop or GitHub Actions runner).
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    /// <summary>Pinned image used by all Redis Testcontainers in this project.</summary>
    public const string RedisImage = "redis:7-alpine";

    private readonly RedisContainer _container = CreateContainer();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to start Redis Testcontainer. " +
                "Docker must be available and running. " +
                "Local: start Docker Desktop. CI: GitHub-hosted ubuntu-latest includes Docker. " +
                "InMemory integration tests do not need Docker.",
                ex);
        }
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    /// <summary>Creates a Redis container with the project-standard image and wait strategy.</summary>
    public static RedisContainer CreateContainer() =>
        new RedisBuilder()
            .WithImage(RedisImage)
            .WithCleanUp(true)
            .Build();
}

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
}
