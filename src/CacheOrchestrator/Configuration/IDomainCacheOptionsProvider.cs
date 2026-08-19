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
    /// <remarks>
    /// If a snapshot for the same normalized domain is already on the request, it is reused.
    /// A different domain replaces the request snapshot (Output Cache headers already queued
    /// for the previous domain are unchanged).
    /// </remarks>
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