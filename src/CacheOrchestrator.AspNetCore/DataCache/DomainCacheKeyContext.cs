using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.DataCache;

/// <summary>Immutable inputs for an HTTP Data Cache key generation operation.</summary>
public sealed class DomainCacheKeyContext
{
    /// <summary>Creates key-generation context.</summary>
    public DomainCacheKeyContext(
        DomainHttpCacheOptions options,
        HttpContext httpContext,
        DomainCacheKeyShape shape = DomainCacheKeyShape.Automatic)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpContext);

        Options = options;
        HttpContext = httpContext;
        Shape = shape;
    }

    /// <summary>Resolved domain options.</summary>
    public DomainHttpCacheOptions Options { get; }

    /// <summary>Current HTTP context.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>Requested key shape.</summary>
    public DomainCacheKeyShape Shape { get; }
}
