using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Resolves effective ASP.NET Core cache policy and pins it to the current request.
/// </summary>
public interface IRequestDomainCacheOptions
{
    /// <summary>Gets or creates the current ASP.NET Core snapshot for a domain.</summary>
    DomainHttpCacheOptions GetOrCreateDomainOptions(string domain);

    /// <summary>
    /// Resolves and stores domain options on the request, reusing a snapshot for the same domain.
    /// </summary>
    DomainHttpCacheOptions EnsureDomainOptions(HttpContext http, string domain);

    /// <summary>Returns the snapshot stored on the request, or null when none was resolved.</summary>
    DomainHttpCacheOptions? GetDomainOptions(HttpContext http);
}
