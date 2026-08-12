namespace CacheOrchestrator.Cluster;

/// <summary>
/// A peer process address for HTTP (or similar) cluster command delivery.
/// </summary>
/// <param name="Id">Stable peer id (should match that peer's <c>Cache:InstanceId</c> when set).</param>
/// <param name="BaseUrl">Base URL of the peer process (scheme + host + port, no path required).</param>
public sealed record ClusterPeer(string Id, Uri BaseUrl);
