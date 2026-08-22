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
    /// Key for the normalized resource id set by <c>IDomainFusionCache.GetOrSetEntityAsync</c>
    /// (and by Output Cache when <c>resourceRouteKey</c> is configured).
    /// </summary>
    public static readonly object ResourceIdKey = new();

    /// <summary>
    /// Key for the normalized entity kind set by <c>IDomainFusionCache.GetOrSetEntityAsync</c>
    /// (and by Output Cache when <c>entityKind</c> is configured).
    /// </summary>
    public static readonly object EntityKindKey = new();

    /// <summary>
    /// Key for a staged <c>EntityFootprint</c> merged into Output Cache tags in
    /// <c>ServeResponseAsync</c> (members / dependsOn / aliases discovered in Fusion factories).
    /// </summary>
    public static readonly object PendingEntityFootprintKey = new();
}