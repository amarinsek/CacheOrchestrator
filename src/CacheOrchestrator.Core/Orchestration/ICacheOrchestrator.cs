using CacheOrchestrator.Entity;

namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Domain-scoped data-cache orchestration (get-or-create with policy, Version, and tags).
/// </summary>
/// <remarks>
/// This is the primary library-facing abstraction for CacheOrchestrator v3.
/// It does not own a store: a registered <see cref="IDataCacheProvider"/> (e.g. FusionCache, HybridCache)
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

    /// <summary>
    /// Footprint-aware get-or-create: stores <see cref="FootprintCacheBox{T}"/>, and on miss
    /// re-<c>Set</c>s with the final expanded tags from the factory footprint.
    /// </summary>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="request">Domain, key, and early footprint/tags (may be expanded by the factory).</param>
    /// <param name="factory">Produces value + footprint (primary / members / dependsOn / aliases).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored box (value + final footprint).</returns>
    ValueTask<FootprintCacheBox<T?>> GetOrCreateWithFootprintAsync<T>(
        CacheEntryRequest request,
        Func<CancellationToken, ValueTask<FootprintCacheBox<T?>>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates one entity entry tagged with <paramref name="primary"/> (and domain).
    /// </summary>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="domain">Cache domain name.</param>
    /// <param name="logicalKey">Logical key material (orchestrator adds domain + Version).</param>
    /// <param name="primary">Primary entity identity for tags.</param>
    /// <param name="factory">Value factory; may return <see langword="null"/> for negative caching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached value, or <see langword="null"/> when the factory produced a miss.</returns>
    ValueTask<T?> GetOrCreateEntityAsync<T>(
        string domain,
        string logicalKey,
        EntityRef primary,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates one entity with an <see cref="EntityCache{T}"/> factory (dependsOn / members / aliases).
    /// </summary>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="domain">Cache domain name.</param>
    /// <param name="logicalKey">Logical key material (orchestrator adds domain + Version).</param>
    /// <param name="primary">Primary entity identity for tags.</param>
    /// <param name="factory">Factory returning value or <see cref="EntityCache.Miss{T}"/> plus footprint extensions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached value, or <see langword="null"/> when the result is a miss.</returns>
    ValueTask<T?> GetOrCreateEntityAsync<T>(
        string domain,
        string logicalKey,
        EntityRef primary,
        Func<CancellationToken, ValueTask<EntityCache<T>>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates a collection entry tagged with member / dependency refs from <see cref="EntitySet{T}"/>.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="domain">Cache domain name.</param>
    /// <param name="logicalKey">Logical key material (orchestrator adds domain + Version).</param>
    /// <param name="entityKind">Default member kind when the set deferred kind to the caller.</param>
    /// <param name="factory">Factory that builds the set and footprint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached collection (empty list when the boxed value is null).</returns>
    ValueTask<IReadOnlyList<T>> GetOrCreateEntitySetAsync<T>(
        string domain,
        string logicalKey,
        string entityKind,
        Func<CancellationToken, ValueTask<EntitySet<T>>> factory,
        CancellationToken cancellationToken = default);
}
