namespace CacheOrchestrator.Invalidation;

/// <summary>Identifies where an invalidation operation originated.</summary>
public enum CacheInvalidationOrigin
{
    /// <summary>The operation was initiated on this process.</summary>
    Local = 0,

    /// <summary>The operation is being applied from a remote cluster command.</summary>
    RemoteCluster = 1
}
