namespace CacheOrchestrator.Cluster;

/// <summary>
/// Empty membership — local-only process (default).
/// </summary>
internal sealed class NullClusterMembership : IClusterMembership
{
    /// <summary>Shared instance for DI and tests.</summary>
    public static readonly NullClusterMembership Instance = new();

    private static readonly IReadOnlyList<ClusterPeer> Empty = [];

    private NullClusterMembership()
    {
    }

    /// <inheritdoc />
    public string Kind => "Null";

    /// <inheritdoc />
    public Task<IReadOnlyList<ClusterPeer>> GetPeersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Empty);
}
