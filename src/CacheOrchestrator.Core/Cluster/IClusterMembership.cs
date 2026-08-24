namespace CacheOrchestrator.Cluster;

/// <summary>
/// Resolves peer instances for the cluster command bus. Discovery only — isolation uses
/// <c>Cache:Namespace</c> on each command.
/// </summary>
public interface IClusterMembership
{
    /// <summary>Optional short label for diagnostics (e.g. <c>Null</c>, <c>Static</c>).</summary>
    string Kind { get; }

    /// <summary>
    /// Returns known peers (may include self; bus excludes self by <c>InstanceId</c>).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClusterPeer>> GetPeersAsync(CancellationToken cancellationToken = default);
}
