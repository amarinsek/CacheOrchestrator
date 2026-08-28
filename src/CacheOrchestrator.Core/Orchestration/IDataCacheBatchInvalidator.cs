namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Optional Data Cache capability for invalidating several instance/tag requests as one bounded operation.
/// </summary>
public interface IDataCacheBatchInvalidator
{
    /// <summary>Invalidates all supplied requests, using provider-native batching or bounded concurrency.</summary>
    ValueTask InvalidateBatchAsync(
        IReadOnlyList<DataCacheInvalidationRequest> requests,
        CancellationToken cancellationToken = default);
}
