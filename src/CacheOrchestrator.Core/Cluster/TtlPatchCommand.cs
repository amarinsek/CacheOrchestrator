namespace CacheOrchestrator.Cluster;

/// <summary>
/// Cluster command that merges runtime TTL overrides on each peer.
/// </summary>
public sealed record TtlPatchCommand : ClusterCommand
{
    /// <summary>Domain name (normalized on apply).</summary>
    public required string Domain { get; init; }

    /// <summary>Output Cache TTL seconds.</summary>
    public int? OutputCacheTtlSeconds { get; init; }

    /// <summary>Data-cache TTL seconds.</summary>
    public int? DataCacheTtlSeconds { get; init; }

    /// <summary>Client TTL seconds.</summary>
    public int? ClientTtlSeconds { get; init; }

    /// <summary>Client min TTL seconds.</summary>
    public int? ClientTtlMinSeconds { get; init; }
}
