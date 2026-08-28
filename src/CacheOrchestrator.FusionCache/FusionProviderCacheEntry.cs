namespace CacheOrchestrator.FusionCache;

internal sealed class FusionProviderCacheEntry<T>
{
    public required T Value { get; init; }

    public required Guid MaterializationId { get; init; }
}
