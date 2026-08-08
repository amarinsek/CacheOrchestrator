using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Resolves effective per-domain cache options for the current request.
/// </summary>
public interface IDomainCacheOptionsProvider
{
    /// <summary>
    /// Ensures domain options are resolved and cached on the request, then returns them.
    /// </summary>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="domain">Raw domain name (normalized internally).</param>
    /// <returns>Effective domain options (never null).</returns>
    DomainCacheOptions EnsureDomainOptions(HttpContext http, string domain);

    /// <summary>
    /// Returns options previously stored on the request, or <see langword="null"/> if none.
    /// </summary>
    /// <param name="http">Current HTTP context.</param>
    /// <returns>Cached options for this request, or null.</returns>
    DomainCacheOptions? GetDomainOptions(HttpContext http);

    /// <summary>
    /// Returns domain options from the process-wide cache (or creates them) without an
    /// <see cref="HttpContext"/>. Useful in contexts where no HTTP request is active,
    /// such as invalidation endpoints.
    /// </summary>
    /// <param name="domain">Raw domain name (normalized internally).</param>
    /// <returns>Effective domain options (never null).</returns>
    DomainCacheOptions GetOrCreateDomainOptions(string domain);
}