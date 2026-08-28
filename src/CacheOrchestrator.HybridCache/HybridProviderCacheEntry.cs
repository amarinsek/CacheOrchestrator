namespace CacheOrchestrator.HybridCache;

internal sealed class HybridProviderCacheEntry<T>
{
    public required T Value { get; init; }

    public required Guid MaterializationId { get; init; }
}
