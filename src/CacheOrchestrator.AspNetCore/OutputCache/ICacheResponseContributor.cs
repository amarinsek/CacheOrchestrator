namespace CacheOrchestrator.OutputCache;

/// <summary>Contributes metadata to a finalized CacheOrchestrator HTTP response.</summary>
/// <remarks>
/// Implementations run immediately before response headers are committed. They should avoid network I/O
/// and complete synchronously whenever possible.
/// </remarks>
public interface ICacheResponseContributor
{
    /// <summary>Contributes response metadata.</summary>
    ValueTask ContributeAsync(
        CacheResponseContext context,
        CancellationToken cancellationToken = default);
}
