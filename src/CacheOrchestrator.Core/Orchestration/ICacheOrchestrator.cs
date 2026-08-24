namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Domain-scoped data-cache orchestration (get-or-create with policy, Version, and tags).
/// </summary>
/// <remarks>
/// This is the primary library-facing abstraction for CacheOrchestrator v3.
/// It does not own a store: a registered <see cref="IDataCacheProvider"/> (e.g. FusionCache)
/// performs the physical get-or-set. HTTP Output Cache / Client Cache live in the ASP.NET
/// integration and are not required to use this interface.
/// </remarks>
public interface ICacheOrchestrator
{
    /// <summary>
    /// Gets a cached value for <paramref name="request"/>, or creates it via <paramref name="factory"/>.
    /// </summary>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="request">Domain, logical key, and optional footprint/tags.</param>
    /// <param name="factory">Value factory invoked on miss or when data cache is disabled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly produced value (may be <see langword="null"/>).</returns>
    ValueTask<T?> GetOrCreateAsync<T>(
        CacheEntryRequest request,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default);
}
