using CacheOrchestrator.Entity;
using System.Globalization;

namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Convenience overloads that take <see cref="CacheDomainContext"/> (host-supplied domain binding).
/// </summary>
public static class CacheOrchestratorDomainContextExtensions
{
    /// <summary>
    /// Gets or creates a value using <paramref name="domain"/>.Domain and <paramref name="logicalKey"/>.
    /// </summary>
    public static ValueTask<T?> GetOrCreateAsync<T>(
        this ICacheOrchestrator cache,
        CacheDomainContext domain,
        string logicalKey,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        ArgumentNullException.ThrowIfNull(factory);

        return cache.GetOrCreateAsync(
            new CacheEntryRequest { Domain = domain.Domain, Key = logicalKey },
            factory,
            cancellationToken);
    }

    /// <summary>
    /// Gets or creates one entity using <paramref name="domain"/>.Domain and
    /// <paramref name="domain"/>.EntityKind (or <paramref name="defaultEntityKind"/>).
    /// </summary>
    public static ValueTask<T?> GetOrCreateEntityAsync<T>(
        this ICacheOrchestrator cache,
        CacheDomainContext domain,
        string logicalKey,
        string resourceId,
        Func<CancellationToken, ValueTask<T?>> factory,
        string defaultEntityKind = "entity",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(factory);

        string kind = domain.EntityKindOr(defaultEntityKind);
        return cache.GetOrCreateEntityAsync(
            domain.Domain,
            logicalKey,
            new EntityRef(kind, resourceId),
            factory,
            cancellationToken);
    }

    /// <summary>
    /// Gets or creates one entity using a naturally typed resource id.
    /// <see cref="IFormattable"/> values are formatted with invariant culture.
    /// </summary>
    public static ValueTask<T?> GetOrCreateEntityAsync<T, TId>(
        this ICacheOrchestrator cache,
        CacheDomainContext domain,
        string logicalKey,
        TId resourceId,
        Func<CancellationToken, ValueTask<T?>> factory,
        string defaultEntityKind = "entity",
        CancellationToken cancellationToken = default)
        where TId : notnull =>
        cache.GetOrCreateEntityAsync(
            domain,
            logicalKey,
            FormatId(resourceId),
            factory,
            defaultEntityKind,
            cancellationToken);

    /// <summary>
    /// Gets or creates one entity with an <see cref="EntityCache{T}"/> factory.
    /// </summary>
    public static ValueTask<T?> GetOrCreateEntityAsync<T>(
        this ICacheOrchestrator cache,
        CacheDomainContext domain,
        string logicalKey,
        string resourceId,
        Func<CancellationToken, ValueTask<EntityCache<T>>> factory,
        string defaultEntityKind = "entity",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(factory);

        string kind = domain.EntityKindOr(defaultEntityKind);
        return cache.GetOrCreateEntityAsync(
            domain.Domain,
            logicalKey,
            new EntityRef(kind, resourceId),
            factory,
            cancellationToken);
    }

    /// <summary>
    /// Gets or creates one entity with an <see cref="EntityCache{T}"/> factory and a naturally typed resource id.
    /// <see cref="IFormattable"/> values are formatted with invariant culture.
    /// </summary>
    public static ValueTask<T?> GetOrCreateEntityAsync<T, TId>(
        this ICacheOrchestrator cache,
        CacheDomainContext domain,
        string logicalKey,
        TId resourceId,
        Func<CancellationToken, ValueTask<EntityCache<T>>> factory,
        string defaultEntityKind = "entity",
        CancellationToken cancellationToken = default)
        where TId : notnull =>
        cache.GetOrCreateEntityAsync(
            domain,
            logicalKey,
            FormatId(resourceId),
            factory,
            defaultEntityKind,
            cancellationToken);

    private static string FormatId<TId>(TId id) where TId : notnull =>
        id switch
        {
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => id.ToString() ?? string.Empty
        };
}
