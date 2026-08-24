namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Data-cache engine behind <see cref="ICacheOrchestrator"/> (FusionCache, HybridCache, …).
/// </summary>
/// <remarks>
/// Not intended as the primary app/library dependency — use <see cref="ICacheOrchestrator"/>.
/// A host should register exactly one implementation.
/// </remarks>
public interface IDataCacheProvider
{
    /// <summary>Stable provider name (e.g. <c>FusionCache</c>, <c>HybridCache</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Gets or creates a value at <paramref name="request"/>.Key with the given tags and domain policy.
    /// </summary>
    ValueTask<T?> GetOrCreateAsync<T>(
        DataCacheProviderRequest request,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>Removes / logically invalidates all entries associated with <paramref name="tag"/>.</summary>
    ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>Removes / logically invalidates entries for each tag in <paramref name="tags"/>.</summary>
    ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);
}
