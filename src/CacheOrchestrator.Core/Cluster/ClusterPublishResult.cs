namespace CacheOrchestrator.Cluster;

/// <summary>Outcome of delivering a <see cref="ClusterCommand"/> to membership peers.</summary>
public sealed class ClusterPublishResult
{
    /// <summary>Creates a publish result for the given peer outcomes.</summary>
    public ClusterPublishResult(IReadOnlyList<ClusterPeerPublishOutcome> peers)
    {
        Peers = peers ?? [];
    }

    /// <summary>Empty success (no peers to contact, or bus no-op).</summary>
    public static ClusterPublishResult Empty { get; } = new([]);

    /// <summary>Per-peer delivery outcomes (excludes self).</summary>
    public IReadOnlyList<ClusterPeerPublishOutcome> Peers { get; }

    /// <summary>
    /// True when every peer succeeded, or there were no peers.
    /// </summary>
    public bool AllSucceeded => Peers.Count == 0 || Peers.All(p => p.Succeeded);

    /// <summary>Peers that did not apply the command.</summary>
    public IEnumerable<ClusterPeerPublishOutcome> Failures => Peers.Where(p => !p.Succeeded);
}

/// <summary>One peer's accept/reject of a cluster command.</summary>
public sealed class ClusterPeerPublishOutcome
{
    /// <summary>Membership peer id.</summary>
    public required string PeerId { get; init; }

    /// <summary>Whether the peer HTTP apply succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Error when <see cref="Succeeded"/> is false.</summary>
    public string? Error { get; init; }
}
