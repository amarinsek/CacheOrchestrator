namespace CacheOrchestrator.Invalidation;

/// <summary>
/// Kind of invalidation operation (for observers and diagnostics).
/// </summary>
public enum CacheInvalidationKind
{
    /// <summary>Single domain tag <c>domain:{name}</c>.</summary>
    Domain = 0,

    /// <summary>Single entity tag <c>entity:{domain}:{id}</c>.</summary>
    Entity = 1,

    /// <summary>Arbitrary tags (all Fusion instances).</summary>
    Tags = 2,

    /// <summary>Multiple domains in one call.</summary>
    Domains = 3
}
