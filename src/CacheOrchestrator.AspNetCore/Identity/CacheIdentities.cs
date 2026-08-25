namespace CacheOrchestrator.Identity;

/// <summary>
/// Built-in identity strategy names for <c>WithCacheIdentity</c> / <c>[CacheIdentity]</c>.
/// </summary>
public static class CacheIdentities
{
    /// <summary>
    /// Explicit Url identity (route / path, query, domain vary) — the same strategy used for
    /// default GET/HEAD when no identity binding is declared.
    /// Use when a non-GET method should use Url-based Output Cache (advanced).
    /// </summary>
    public const string Url = "__cache_orchestrator.url__";
}
