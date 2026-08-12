namespace CacheOrchestrator.EFCore;

/// <summary>
/// What to invalidate when a mapped group in one SaveChanges is at least <c>BulkThreshold</c> ids.
/// </summary>
public enum EfCoreOnBulk
{
    /// <summary>Always evict the individual entity tags (no escape hatch).</summary>
    Entities = 0,

    /// <summary>Evict <c>entitykind:{domain}:{entityKind}</c> (recommended; does not wipe sibling kinds).</summary>
    Kind = 1,

    /// <summary>Evict the whole domain. Wipes every kind that shares that policy group.</summary>
    Domain = 2
}
