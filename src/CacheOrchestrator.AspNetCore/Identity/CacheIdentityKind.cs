namespace CacheOrchestrator.Identity;

/// <summary>
/// How an endpoint method builds cache identity.
/// </summary>
public enum CacheIdentityKind
{
    /// <summary>Route / path / query / domain vary only (default GET/HEAD behaviour).</summary>
    Url = 0,

    /// <summary>Named <see cref="ICacheIdentityContract"/> resolved from DI onto endpoint metadata.</summary>
    NamedContract = 1,

    /// <summary>Bounded request-body XxHash3 (see <c>WithContentHashCacheIdentity</c>).</summary>
    ContentHash = 2,
}
