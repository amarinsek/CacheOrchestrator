namespace CacheOrchestrator.Cluster;

/// <summary>
/// Cluster command that applies a process-local runtime Version overlay on each peer.
/// </summary>
public sealed record VersionBumpCommand : ClusterCommand
{
    /// <summary>Domain name (normalized on apply).</summary>
    public required string Domain { get; init; }

    /// <summary>New version token.</summary>
    public required string Version { get; init; }
}
