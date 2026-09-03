using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.OutputCache;

/// <summary>Finalized cache metadata exposed to optional HTTP response contributors.</summary>
public sealed class CacheResponseContext
{
    /// <summary>Initializes a finalized response context.</summary>
    public CacheResponseContext(
        HttpContext httpContext,
        DomainHttpCacheOptions domainOptions,
        bool sharedCacheEligible,
        IReadOnlyList<string> tags,
        OutputCacheResult outputCacheResult = OutputCacheResult.Miss)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(domainOptions);
        ArgumentNullException.ThrowIfNull(tags);

        HttpContext = httpContext;
        DomainOptions = domainOptions;
        SharedCacheEligible = sharedCacheEligible;
        Tags = tags;
        OutputCacheResult = outputCacheResult;
    }

    /// <summary>The current HTTP context.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The resolved CacheOrchestrator domain policy.</summary>
    public DomainHttpCacheOptions DomainOptions { get; }

    /// <summary>Whether the finalized response is eligible for storage in a shared cache.</summary>
    public bool SharedCacheEligible { get; }

    /// <summary>Canonical domain/entity tags associated with the response.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>The finalized Output Cache result for the response.</summary>
    public OutputCacheResult OutputCacheResult { get; }
}
