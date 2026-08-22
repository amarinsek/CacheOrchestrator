using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.FusionCache;

/// <summary>
/// High-level FusionCache API scoped to the current request's cache domain.
/// </summary>
public interface IDomainFusionCache
{
    /// <summary>
    /// Gets a cached value for the current request domain, or creates it via <paramref name="factory"/>.
    /// </summary>
    /// <remarks>
    /// Domain-scoped: one entry whose identity is the request (path/query/version).
    /// Tagged <c>domain:{domain}</c> only — not evicted by <c>InvalidateEntityAsync</c>.
    /// Domain resolution order when options are not yet on the request:
    /// <list type="number">
    /// <item>Endpoint metadata (<see cref="OutputCache.DomainOutputCachePolicy"/> / <see cref="OutputCache.CacheDomainAttribute"/>)</item>
    /// <item>If still missing, runs the factory uncached and logs a Warning with metric
    /// <c>result=unresolved</c> (Fusion-only endpoints should use the domain overload or
    /// <see cref="Configuration.IDomainCacheOptionsProvider.EnsureDomainOptions"/>).</item>
    /// </list>
    /// When <c>.CacheOutputWithDomain(...)</c> or <c>[CacheDomain]</c> is used, the Output Cache policy usually
    /// already called <c>EnsureDomainOptions</c> before the handler runs.
    /// </remarks>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="factory">Value factory invoked on cache miss (or when caching is disabled).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly produced value.</returns>
    Task<T> GetOrSetAsync<T>(
        HttpContext http,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a cached value for the specified <paramref name="domain"/>, ensuring domain options on the request first.
    /// </summary>
    /// <remarks>
    /// Prefer this overload (or <see cref="Configuration.IDomainCacheOptionsProvider.EnsureDomainOptions"/>)
    /// for <strong>Fusion-only</strong> endpoints that do not use Output Cache / <c>CacheOutputWithDomain</c>.
    /// If options for the same domain are already on the request, they are reused.
    /// A different explicit domain replaces the request snapshot so
    /// <c>GetOrSetAsync(http, "products")</c> and <c>GetOrSetAsync(http, "catalog")</c> never share an entry.
    /// Domain-scoped (list/snapshot): no entity tags.
    /// </remarks>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="domain">Cache domain name (normalized via <c>EnsureDomainOptions</c>).</param>
    /// <param name="factory">Value factory invoked on cache miss (or when caching is disabled).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly produced value.</returns>
    Task<T> GetOrSetAsync<T>(
        HttpContext http,
        string domain,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets one entity using primary identity already on the request
    /// (<c>[CacheDomain]</c> / <c>CacheOutputWithDomain</c> with <c>resourceRouteKey</c>, or <see cref="SetEntityIdentity"/>).
    /// </summary>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="factory">Value factory; may return <see langword="null"/> for negative caching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached value, or <see langword="null"/> when the factory produced a miss.</returns>
    /// <exception cref="InvalidOperationException">Thrown when entity kind/id are not on the request.</exception>
    Task<T?> GetOrSetEntityAsync<T>(
        HttpContext http,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets one entity with an <see cref="EntityCache{T}"/> factory (dependsOn / members / aliases).
    /// Primary identity comes from the request.
    /// </summary>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="factory">Factory returning value or <see cref="EntityCache.Miss{T}"/> plus footprint extensions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached value, or <see langword="null"/> when the result is a miss.</returns>
    /// <exception cref="InvalidOperationException">Thrown when entity kind/id are not on the request.</exception>
    Task<T?> GetOrSetEntityAsync<T>(
        HttpContext http,
        Func<CancellationToken, Task<EntityCache<T>>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets a collection (URL-shaped key) tagged with member / dependency entity refs from
    /// <see cref="EntitySet{T}"/>. Requires entity kind on the request (kind-scoped endpoint metadata).
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="factory">Factory that builds the set and footprint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached collection.</returns>
    /// <exception cref="InvalidOperationException">Thrown when entity kind is not on the request.</exception>
    Task<IReadOnlyList<T>> GetOrSetEntitySetAsync<T>(
        HttpContext http,
        Func<CancellationToken, Task<EntitySet<T>>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets entity identity on the request for Fusion-only endpoints (no Output Cache entity metadata).
    /// </summary>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    /// <param name="resourceId">Stable business id.</param>
    void SetEntityIdentity(HttpContext http, string entityKind, string resourceId);

    /// <summary>
    /// Gets or sets one entity (row) using the domain already on the request / endpoint.
    /// </summary>
    /// <remarks>
    /// Obsolete: prefer <see cref="GetOrSetEntityAsync{T}(HttpContext, Func{CancellationToken, Task{T?}}, CancellationToken)"/>
    /// with identity from endpoint metadata or <see cref="SetEntityIdentity"/>.
    /// </remarks>
    [Obsolete("Use GetOrSetEntityAsync(http, factory). Identity comes from CacheOutputWithDomain / [CacheDomain] or SetEntityIdentity.")]
    Task<T> GetOrSetEntityAsync<T>(
        HttpContext http,
        string entityKind,
        string resourceId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets one entity (row) for a specific <paramref name="domain"/>.
    /// </summary>
    /// <remarks>
    /// Obsolete: prefer endpoint metadata (or <see cref="SetEntityIdentity"/>) with
    /// <see cref="GetOrSetEntityAsync{T}(HttpContext, Func{CancellationToken, Task{T?}}, CancellationToken)"/>.
    /// </remarks>
    [Obsolete("Use GetOrSetEntityAsync(http, factory). Identity comes from CacheOutputWithDomain / [CacheDomain] or SetEntityIdentity.")]
    Task<T> GetOrSetEntityAsync<T>(
        HttpContext http,
        string domain,
        string entityKind,
        string resourceId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);
}