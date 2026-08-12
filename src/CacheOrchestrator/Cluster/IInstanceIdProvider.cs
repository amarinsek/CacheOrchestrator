namespace CacheOrchestrator.Cluster;

/// <summary>
/// Stable process identity for Admin, cluster bus anti-echo, and diagnostics.
/// Bound from <c>Cache:InstanceId</c> (fallback: machine name).
/// </summary>
public interface IInstanceIdProvider
{
    /// <summary>Resolved instance id for this process.</summary>
    string InstanceId { get; }
}
