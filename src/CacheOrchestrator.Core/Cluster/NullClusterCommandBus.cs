namespace CacheOrchestrator.Cluster;

/// <summary>
/// No-op bus used when <c>CacheOrchestrator.HttpBus</c> is not registered or cluster bus is disabled.
/// </summary>
public sealed class NullClusterCommandBus : IClusterCommandBus
{
    /// <summary>Shared instance for DI and tests.</summary>
    public static readonly NullClusterCommandBus Instance = new();

    private NullClusterCommandBus()
    {
    }

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public Task<ClusterPublishResult> PublishAsync(ClusterCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Task.FromResult(ClusterPublishResult.Empty);
    }
}
