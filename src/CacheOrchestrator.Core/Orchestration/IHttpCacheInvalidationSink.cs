namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Optional HTTP Output Cache eviction sink used by the invalidator.
/// </summary>
/// <remarks>
/// Registered by the ASP.NET integration. Core/data-only hosts can omit it or use a no-op.
/// </remarks>
public interface IHttpCacheInvalidationSink
{
    /// <summary>Evicts Output Cache entries associated with <paramref name="tag"/>.</summary>
    ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken = default);
}
