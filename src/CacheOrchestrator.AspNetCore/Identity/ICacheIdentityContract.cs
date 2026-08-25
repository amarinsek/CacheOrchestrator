namespace CacheOrchestrator.Identity;

/// <summary>
/// Named, reusable extractor that builds stable cache-identity material for an HTTP request.
/// Register with <c>AddCacheIdentityContract&lt;T&gt;()</c>; instances are resolved onto endpoint metadata at startup.
/// </summary>
public interface ICacheIdentityContract
{
    /// <summary>
    /// Unique contract name used by <c>WithCacheIdentity</c> / <c>[CacheIdentity]</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Builds identity material for the current request.
    /// Return <see langword="null"/> to skip caching for this request.
    /// </summary>
    ValueTask<CacheIdentityMaterial?> BuildAsync(
        CacheIdentityContext context,
        CancellationToken cancellationToken);
}
