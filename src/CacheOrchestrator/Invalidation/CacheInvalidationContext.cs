namespace CacheOrchestrator.Invalidation;

/// <summary>
/// Describes an invalidation operation for <see cref="ICacheInvalidationObserver"/> callbacks.
/// </summary>
public sealed class CacheInvalidationContext
{
    /// <summary>
    /// Initializes a new context.
    /// </summary>
    public CacheInvalidationContext(
        CacheInvalidationKind kind,
        string scope,
        IReadOnlyList<string> tags)
    {
        Kind = kind;
        Scope = scope ?? string.Empty;
        Tags = tags ?? [];
    }

    /// <summary>Operation kind.</summary>
    public CacheInvalidationKind Kind { get; }

    /// <summary>Human-readable scope (domain, domain/id, joined tags).</summary>
    public string Scope { get; }

    /// <summary>Tags that will be / were evicted.</summary>
    public IReadOnlyList<string> Tags { get; }
}
