using CacheOrchestrator.Configuration;
using CacheOrchestrator.Vary;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.Identity;

/// <summary>
/// Request context passed to <see cref="ICacheIdentityContract.BuildAsync"/>.
/// </summary>
public sealed class CacheIdentityContext
{
    /// <summary>Current HTTP context.</summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>Resolved domain options for the request.</summary>
    public required DomainHttpCacheOptions Options { get; init; }

    /// <summary>Whether material is being built for Output Cache or data-cache keys.</summary>
    public required CacheVarySurface Surface { get; init; }
}
