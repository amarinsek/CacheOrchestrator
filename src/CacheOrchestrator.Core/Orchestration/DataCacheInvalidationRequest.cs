namespace CacheOrchestrator.Orchestration;

/// <summary>Tag invalidation requested from a Data Cache provider.</summary>
public sealed class DataCacheInvalidationRequest
{
    /// <summary>Tags to invalidate.</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>Named instance to target, or <see langword="null"/> for every configured instance.</summary>
    public string? InstanceName { get; init; }
}
