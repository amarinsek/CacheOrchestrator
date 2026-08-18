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
    /// Delivers <paramref name="command"/> to peers (excluding self) and returns per-peer outcomes.
    /// Implementations should not throw for individual peer HTTP/timeout failures — report them in
    /// <see cref="ClusterPublishResult"/>. Transport/setup failures may still throw.
    /// </summary>
    /// <param name="command">Command to publish (must not carry cache payloads).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClusterPublishResult> PublishAsync(ClusterCommand command, CancellationToken cancellationToken = default);
}
