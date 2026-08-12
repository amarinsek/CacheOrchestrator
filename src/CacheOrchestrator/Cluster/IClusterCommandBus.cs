namespace CacheOrchestrator.Cluster;

/// <summary>
/// Publishes cluster commands to peer instances. Default implementation is a no-op
/// (<see cref="NullClusterCommandBus"/>) until <c>CacheOrchestrator.Bus</c> is registered.
/// </summary>
public interface IClusterCommandBus
{
    /// <summary>
    /// When <see langword="false"/>, callers must not invoke <see cref="PublishAsync"/>
    /// (Null bus / disabled configuration). Hot paths never depend on the bus.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Delivers <paramref name="command"/> to peers (excluding self). Best-effort; implementations
    /// should not throw for partial peer failures (log/metrics instead).
    /// </summary>
    /// <param name="command">Command to publish (must not carry cache payloads).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(ClusterCommand command, CancellationToken cancellationToken = default);
}
