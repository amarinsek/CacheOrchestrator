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
    /// Invalidates a single entity (tag <c>entity:{domain}:{entityKind}:{resourceId}</c>)
    /// on the FusionCache instance that owns the domain and on Output Cache.
    /// </summary>
    /// <remarks>
    /// Entries must have been stored with that entity tag (via <c>GetOrSetEntityAsync</c>
    /// and/or Output Cache <c>resourceRouteKey</c> + <c>entityKind</c>). Does not bump <c>Version</c>.
    /// A domain is a policy group; <paramref name="entityKind"/> is required because ids are not unique in a domain.
    /// </remarks>
    /// <param name="domain">Cache domain name.</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    /// <param name="resourceId">Stable business id (e.g. product id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured outcome.</returns>
    ValueTask<CacheInvalidationResult> InvalidateEntityAsync(
        string domain,
        string entityKind,
        string resourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates many entities of the same kind in one call (one Bus publish).
    /// </summary>
    /// <param name="domain">Cache domain name.</param>
    /// <param name="entityKind">Resource type within the domain.</param>
    /// <param name="resourceIds">Resource ids (null/whitespace entries ignored; duplicates removed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured outcome.</returns>
    ValueTask<CacheInvalidationResult> InvalidateEntitiesAsync(
        string domain,
        string entityKind,
        IEnumerable<string> resourceIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates every entry tagged for an entity kind (<c>entitykind:{domain}:{entityKind}</c>).
    /// </summary>
    /// <remarks>
    /// Does not purge other kinds in the same domain. List/index entries are evicted only if they
    /// were written with the kind tag.
    /// </remarks>
    /// <param name="domain">Cache domain name.</param>
    /// <param name="entityKind">Resource type within the domain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured outcome.</returns>
    ValueTask<CacheInvalidationResult> InvalidateEntityKindAsync(
        string domain,
        string entityKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts the given tags from every registered FusionCache instance and from Output Cache.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="InvalidateDomainAsync"/> / <see cref="InvalidateEntityAsync"/> when possible.
    /// Use this for custom tags you attached yourself.
    /// </remarks>
    /// <param name="tags">Tag strings (e.g. <c>domain:store</c>, <c>entity:store:products:42</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured outcome.</returns>
    ValueTask<CacheInvalidationResult> InvalidateTagsAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default);
}
