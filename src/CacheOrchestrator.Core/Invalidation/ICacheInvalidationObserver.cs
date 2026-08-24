namespace CacheOrchestrator.Invalidation;

/// <summary>
/// Optional hook for audit, webhooks, or metrics around cache invalidation.
/// Register one or more implementations in DI; they run in registration order.
/// </summary>
/// <remarks>
/// Observers must not throw for normal control flow — exceptions are logged and do not
/// fail the invalidation itself.
/// </remarks>
public interface ICacheInvalidationObserver
{
    /// <summary>
    /// Called immediately before data-cache / Output Cache eviction starts.
    /// </summary>
    ValueTask OnBeforeInvalidateAsync(
        CacheInvalidationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after eviction attempts finish (even on partial failure).
    /// </summary>
    ValueTask OnAfterInvalidateAsync(
        CacheInvalidationContext context,
        CacheInvalidationResult result,
        CancellationToken cancellationToken = default);
}
