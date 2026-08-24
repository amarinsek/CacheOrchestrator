using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.Vary;

/// <summary>
/// Context passed to <see cref="ICacheVaryContributor"/> implementations.
/// </summary>
public sealed class CacheVaryContext
{
    /// <summary>Current HTTP request.</summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>Resolved domain options for this request.</summary>
    public required DomainCacheOptions Options { get; init; }

    /// <summary>Which cache surface is building vary material.</summary>
    public required CacheVarySurface Surface { get; init; }
}
