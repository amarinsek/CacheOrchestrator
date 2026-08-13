using CacheOrchestrator.Invalidation;

namespace CacheOrchestrator.Cluster;

/// <summary>
/// Cluster command that replays a tag-based invalidation on each peer (ApplyLocal only).
/// </summary>
public sealed record InvalidateCommand : ClusterCommand
{
    /// <summary>Original invalidation kind (domain / entity / entityKind / tags).</summary>
    public required CacheInvalidationKind Kind { get; init; }

    /// <summary>Human-readable scope (domain, domain/kind/id, or joined tags).</summary>
    public required string Scope { get; init; }

    /// <summary>Tags to evict (fallback apply path on peers).</summary>
    public required string[] Tags { get; init; }

    /// <summary>Domain name when kind is domain, entity, or entityKind.</summary>
    public string? Domain { get; init; }

    /// <summary>Entity kind when kind is entity or entityKind.</summary>
    public string? EntityKind { get; init; }

    /// <summary>Single entity resource id when kind is entity.</summary>
    public string? EntityId { get; init; }

    /// <summary>Batch resource ids when kind is entity.</summary>
    public IReadOnlyList<string>? ResourceIds { get; init; }
}
