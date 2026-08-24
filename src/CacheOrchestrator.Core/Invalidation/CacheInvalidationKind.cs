namespace CacheOrchestrator.Invalidation;

/// <summary>
/// Kind of invalidation operation (for observers and diagnostics).
/// </summary>
public enum CacheInvalidationKind
{
    /// <summary>Single domain tag <c>domain:{name}</c>.</summary>
    Domain = 0,

    /// <summary>Entity tag(s) <c>entity:{domain}:{entityKind}:{id}</c>.</summary>
    Entity = 1,

    /// <summary>Arbitrary tags (all data-cache instances).</summary>
    Tags = 2,

    /// <summary>Multiple domains in one call.</summary>
    Domains = 3,

    /// <summary>Kind-wide tag <c>entitykind:{domain}:{entityKind}</c>.</summary>
    EntityKind = 4
}
