using System.Globalization;

namespace CacheOrchestrator.Invalidation;

/// <summary>
/// Generic extensions for <see cref="ICacheOrchestratorInvalidator"/> to avoid manual <c>.ToString()</c> calls.
/// </summary>
public static class ICacheOrchestratorInvalidatorExtensions
{
    /// <summary>
    /// Invalidates a single entity using a generic resource id.
    /// </summary>
    public static ValueTask<CacheInvalidationResult> InvalidateEntityAsync<TId>(
        this ICacheOrchestratorInvalidator invalidator,
        string domain,
        string entityKind,
        TId resourceId,
        CancellationToken cancellationToken = default) where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(invalidator);
        return invalidator.InvalidateEntityAsync(domain, entityKind, FormatId(resourceId), cancellationToken);
    }

    /// <summary>
    /// Invalidates many entities using generic resource ids.
    /// </summary>
    public static ValueTask<CacheInvalidationResult> InvalidateEntitiesAsync<TId>(
        this ICacheOrchestratorInvalidator invalidator,
        string domain,
        string entityKind,
        IEnumerable<TId> resourceIds,
        CancellationToken cancellationToken = default) where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(invalidator);
        ArgumentNullException.ThrowIfNull(resourceIds);
        return invalidator.InvalidateEntitiesAsync(domain, entityKind, resourceIds.Select(FormatId), cancellationToken);
    }

    private static string FormatId<TId>(TId id) where TId : notnull
    {
        return id switch
        {
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => id.ToString() ?? string.Empty
        };
    }
}
