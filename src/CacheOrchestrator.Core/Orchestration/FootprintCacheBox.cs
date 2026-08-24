using CacheOrchestrator.Entity;

namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Stored payload that keeps the invalidation footprint beside the value
/// so callers (e.g. ASP.NET Output Cache) can stage late tags on cache hits.
/// </summary>
/// <typeparam name="T">Cached value type (may be nullable).</typeparam>
public sealed class FootprintCacheBox<T>
{
    /// <summary>Cached value; may be <see langword="default"/> when <see cref="IsMiss"/> is <see langword="true"/>.</summary>
    public required T? Value { get; init; }

    /// <summary>Invalidation footprint persisted with the entry.</summary>
    public required EntityFootprint Footprint { get; init; }

    /// <summary>When <see langword="true"/>, the factory produced a negative-cache / miss result.</summary>
    public bool IsMiss { get; init; }
}
