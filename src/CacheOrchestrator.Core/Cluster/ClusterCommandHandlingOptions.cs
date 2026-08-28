namespace CacheOrchestrator.Cluster;

internal sealed class ClusterCommandHandlingOptions
{
    public int DedupeWindowSeconds { get; set; } = 60;
}
