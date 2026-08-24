using CacheOrchestrator.Entity;
namespace CacheOrchestrator.FusionCache;

/// <summary>
/// Internal Fusion payload that keeps the invalidation footprint beside the value
/// so Output Cache can stage late tags on Fusion hits.
/// </summary>
internal sealed class FootprintCacheBox<T>
{
    public required T? Value { get; init; }

    public required EntityFootprint Footprint { get; init; }

    public bool IsMiss { get; init; }
}