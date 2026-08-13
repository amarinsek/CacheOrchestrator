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
    /// If options are already present on the request, they are reused.
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
    /// Gets or sets one entity (row) using the domain already on the request / endpoint.
    /// </summary>
    /// <remarks>
    /// Happy path when <c>.CacheOutputWithDomain</c> / <c>[CacheDomain]</c> already set the domain.
    /// <paramref name="entityKind"/> is required — a domain is a policy group, not an id namespace.
    /// </remarks>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    /// <param name="resourceId">Stable business id. Must not be null or whitespace.</param>
    /// <param name="factory">Value factory invoked on cache miss.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly produced value.</returns>
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
    /// Ensures domain options, stores the normalized kind and resource id on the request, includes both
    /// in the Fusion key, and tags the entry with <c>domain:{domain}</c>,
    /// <c>entity:{domain}:{entityKind}:{resourceId}</c>, and <c>entitykind:{domain}:{entityKind}</c>.
    /// After an update, call <see cref="Invalidation.ICacheOrchestratorInvalidator.InvalidateEntityAsync"/>
    /// with the same domain, kind, and id.
    /// </remarks>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="domain">Cache domain name.</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    /// <param name="resourceId">Stable business id. Must not be null or whitespace.</param>
    /// <param name="factory">Value factory invoked on cache miss.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly produced value.</returns>
    Task<T> GetOrSetEntityAsync<T>(
        HttpContext http,
        string domain,
        string entityKind,
        string resourceId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);
}
