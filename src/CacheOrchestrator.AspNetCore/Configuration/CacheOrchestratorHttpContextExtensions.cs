using CacheOrchestrator.Configuration;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Extension methods for interacting with CacheOrchestrator state on the <see cref="HttpContext"/>.
/// </summary>
public static class CacheOrchestratorHttpContextExtensions
{
    /// <summary>
    /// Gets the resolved <see cref="DomainHttpCacheOptions"/> for the current request, if the request
    /// has been processed by the CacheOrchestrator Output Cache policy or if <c>EnsureDomainOptions</c> was called.
    /// </summary>
    /// <param name="httpContext">The HTTP context.</param>
    /// <returns>The resolved domain options, or null if a domain was not resolved for this request.</returns>
    public static DomainHttpCacheOptions? GetDomainCacheOptions(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Features.Get<ICacheOrchestratorFeature>()?.DomainOptions;
    }
}
