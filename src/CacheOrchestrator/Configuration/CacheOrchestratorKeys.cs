namespace CacheOrchestrator.Configuration;

/// <summary>
/// Shared keys for <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> used across CacheOrchestrator components.
/// </summary>
public static class CacheOrchestratorKeys
{
    /// <summary>
    /// Key used to store <see cref="CacheDisposition"/> for the current request.
    /// </summary>
    public static readonly object DispositionKey = new();

    /// <summary>
    /// Key for the resolved <see cref="DomainCacheOptions"/> snapshot on the current request
    /// (set by Output Cache policy / <see cref="IDomainCacheOptionsProvider.EnsureDomainOptions"/>).
    /// </summary>
    public static readonly object DomainOptionsKey = new();

    /// <summary>
    /// Key for the normalized resource id set by <c>IDomainFusionCache.GetOrSetAsync</c> resource overloads
    /// (and optionally used by key generation / entity tags).
    /// </summary>
    public static readonly object ResourceIdKey = new();
}