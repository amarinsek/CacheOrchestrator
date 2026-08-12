using CacheOrchestrator.Invalidation;

namespace CacheOrchestrator.Cluster;

/// <summary>
/// Cluster command that replays a tag-based invalidation on each peer (ApplyLocal only).
/// </summary>
public sealed record InvalidateCommand : ClusterCommand
{
    /// <summary>Original invalidation kind (domain / entity / tags).</summary>
    public required CacheInvalidationKind Kind { get; init; }

    /// <summary>Human-readable scope (domain, domain/id, or joined tags).</summary>
    public required string Scope { get; init; }

    /// <summary>Tags to evict (preferred apply path on peers).</summary>
    public required string[] Tags { get; init; }

    /// <summary>Domain name when kind is domain or entity.</summary>
    public string? Domain { get; init; }

    /// <summary>Entity resource id when kind is entity.</summary>
    public string? EntityId { get; init; }
}
