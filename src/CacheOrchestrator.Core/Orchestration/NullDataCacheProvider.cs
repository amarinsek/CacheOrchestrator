namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Pass-through data-cache provider used when no Fusion/Hybrid package is registered
/// (Output Cache–only hosts). Factories always run; tag removes are no-ops.
/// </summary>
internal sealed class NullDataCacheProvider : IDataCacheProvider
{
    /// <summary>Shared instance for DI and tests.</summary>
    public static readonly NullDataCacheProvider Instance = new();

    private NullDataCacheProvider()
    {
    }

    /// <inheritdoc />
    public string Name => "Null";

    /// <inheritdoc />
    public async ValueTask<T> GetOrCreateAsync<T>(
        DataCacheProviderRequest request,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(factory);
        return await factory(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask SetAsync<T>(
        DataCacheProviderRequest request,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask InvalidateAsync(
        DataCacheInvalidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ValueTask.CompletedTask;
    }
}
