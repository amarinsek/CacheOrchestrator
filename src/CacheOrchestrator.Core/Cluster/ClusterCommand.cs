namespace CacheOrchestrator.Cluster;

/// <summary>
/// Transport-independent operation published to cluster peers (never carries cache payloads).
/// Transport packages own their wire format and protocol versioning.
/// </summary>
public abstract record ClusterCommand
{
    /// <summary>Unique id for this command instance (idempotency / diagnostics).</summary>
    public required Guid CommandId { get; init; }

    /// <summary>Origin process id (<c>Cache:InstanceId</c> or machine name).</summary>
    public required string OriginInstanceId { get; init; }

    /// <summary>Root <c>Cache:Namespace</c> — isolation boundary; peers with another namespace ignore the command.</summary>
    public required string Namespace { get; init; }

    /// <summary>UTC timestamp when the origin created the command.</summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Optional correlation id for tracing across nodes.</summary>
    public string? CorrelationId { get; init; }
}
