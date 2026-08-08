namespace CacheOrchestrator.Invalidation;

/// <summary>
/// Provides programmatic cache invalidation for CacheOrchestrator domains and tags.
/// </summary>
/// <remarks>
/// All methods are <strong>best-effort</strong>: they do not throw when Fusion or Output Cache
/// fails; inspect <see cref="CacheInvalidationResult"/> instead. Register
/// <see cref="ICacheInvalidationObserver"/> for audit/webhook hooks.
/// </remarks>
public interface ICacheOrchestratorInvalidator
{
    /// <summary>
    /// Invalidates all cache entries (Output Cache and FusionCache) tagged for the domain
    /// (<c>domain:{name}</c>).
    /// </summary>
    /// <param name="domain">The domain name (will be normalized).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured outcome (Fusion/Output success flags and errors).</returns>
    ValueTask<CacheInvalidationResult> InvalidateDomainAsync(
        string domain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates multiple domains sequentially (each domain on its owning Fusion instance).
    /// </summary>
    /// <param name="domains">Domain names (null/whitespace entries ignored).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated outcome across all domains.</returns>
    ValueTask<CacheInvalidationResult> InvalidateDomainsAsync(
        IEnumerable<string> domains,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates a single entity within a domain (tag <c>entity:{domain}:{resourceId}</c>)
    /// on the FusionCache instance that owns the domain and on Output Cache.
    /// </summary>
    /// <remarks>
    /// Entries must have been stored with that entity tag (via <c>GetOrSetAsync</c> resource overloads
    /// and/or Output Cache <c>resourceRouteKey</c>). Does not bump <c>Version</c>.
    /// </remarks>
    /// <param name="domain">Cache domain name.</param>
    /// <param name="resourceId">Stable business id (e.g. product id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured outcome.</returns>
    ValueTask<CacheInvalidationResult> InvalidateEntityAsync(
        string domain,
        string resourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts the given tags from every registered FusionCache instance and from Output Cache.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="InvalidateDomainAsync"/> / <see cref="InvalidateEntityAsync"/> when possible.
    /// Use this for custom tags you attached yourself.
    /// </remarks>
    /// <param name="tags">Tag strings (e.g. <c>domain:products</c>, <c>entity:products:42</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured outcome.</returns>
    ValueTask<CacheInvalidationResult> InvalidateTagsAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default);
}
