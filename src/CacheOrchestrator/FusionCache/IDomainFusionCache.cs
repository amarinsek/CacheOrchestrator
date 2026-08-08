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
    /// Gets or sets a value for a specific <paramref name="resourceId"/> within <paramref name="domain"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ensures domain options, stores the normalized resource id on the request, includes it in the Fusion key,
    /// and tags the entry with <c>domain:{domain}</c> and <c>entity:{domain}:{resourceId}</c>.
    /// </para>
    /// <para>
    /// After an update, call
    /// <see cref="Invalidation.ICacheOrchestratorInvalidator.InvalidateEntityAsync"/> with the same domain and id
    /// — no <c>Version</c> bump required. Use this for dynamic / CRUD resources; snapshot domains (e.g. tiles)
    /// can keep using the overloads without a resource id.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">Cached value type.</typeparam>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="domain">Cache domain name.</param>
    /// <param name="resourceId">Stable business id (e.g. product id). Must not be null or whitespace.</param>
    /// <param name="factory">Value factory invoked on cache miss.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly produced value.</returns>
    Task<T> GetOrSetAsync<T>(
        HttpContext http,
        string domain,
        string resourceId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);
}
