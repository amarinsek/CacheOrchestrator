using CacheOrchestrator.Configuration;
using CacheOrchestrator.Entity;

namespace CacheOrchestrator.Orchestration;

/// <summary>Entity-oriented convenience operations built on the stable orchestrator primitives.</summary>
public static class CacheOrchestratorEntityExtensions
{
    /// <summary>Gets or creates one entity entry tagged with <paramref name="primary"/>.</summary>
    public static ValueTask<T?> GetOrCreateEntityAsync<T>(
        this ICacheOrchestrator cache,
        string domain,
        string logicalKey,
        EntityRef primary,
        Func<CancellationToken, ValueTask<T?>> factory) =>
        GetOrCreateEntityAsync(cache, domain, logicalKey, primary, factory, CancellationToken.None);

    /// <summary>Gets or creates one entity entry tagged with <paramref name="primary"/>.</summary>
    public static async ValueTask<T?> GetOrCreateEntityAsync<T>(
        this ICacheOrchestrator cache,
        string domain,
        string logicalKey,
        EntityRef primary,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken)
    {
        Validate(cache, domain, logicalKey, primary, factory);
        EntityFootprint early = new(primary);
        FootprintCacheBox<T?> box = await cache.GetOrCreateWithFootprintAsync<T>(
                new CacheEntryRequest { Domain = domain, Key = logicalKey, Footprint = early },
                async token =>
                {
                    T? value = await factory(token).ConfigureAwait(false);
                    return new FootprintCacheBox<T?>
                    {
                        Value = value,
                        IsMiss = value is null,
                        Footprint = early
                    };
                },
                cancellationToken)
            .ConfigureAwait(false);

        return box.IsMiss ? default : box.Value;
    }

    /// <summary>Gets or creates one entity whose factory can expand its invalidation footprint.</summary>
    public static ValueTask<T?> GetOrCreateEntityAsync<T>(
        this ICacheOrchestrator cache,
        string domain,
        string logicalKey,
        EntityRef primary,
        Func<CancellationToken, ValueTask<EntityCache<T>>> factory) =>
        GetOrCreateEntityAsync(cache, domain, logicalKey, primary, factory, CancellationToken.None);

    /// <summary>Gets or creates one entity whose factory can expand its invalidation footprint.</summary>
    public static async ValueTask<T?> GetOrCreateEntityAsync<T>(
        this ICacheOrchestrator cache,
        string domain,
        string logicalKey,
        EntityRef primary,
        Func<CancellationToken, ValueTask<EntityCache<T>>> factory,
        CancellationToken cancellationToken)
    {
        Validate(cache, domain, logicalKey, primary, factory);
        FootprintCacheBox<T?> box = await cache.GetOrCreateWithFootprintAsync<T>(
                new CacheEntryRequest
                {
                    Domain = domain,
                    Key = logicalKey,
                    Footprint = new EntityFootprint(primary)
                },
                async token =>
                {
                    EntityCache<T> produced = await factory(token).ConfigureAwait(false);
                    ArgumentNullException.ThrowIfNull(produced);
                    EntityFootprint full = (produced.Footprint ?? EntityFootprint.Empty).WithPrimary(primary);
                    return new FootprintCacheBox<T?>
                    {
                        Value = produced.IsMiss ? default : produced.Value,
                        IsMiss = produced.IsMiss,
                        Footprint = full
                    };
                },
                cancellationToken)
            .ConfigureAwait(false);

        return box.IsMiss ? default : box.Value;
    }

    /// <summary>Gets or creates a collection tagged with its members and dependencies.</summary>
    public static async ValueTask<IReadOnlyList<T>> GetOrCreateEntitySetAsync<T>(
        this ICacheOrchestrator cache,
        string domain,
        string logicalKey,
        string entityKind,
        Func<CancellationToken, ValueTask<EntitySet<T>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentNullException.ThrowIfNull(factory);

        string normalizedKind = DomainName.NormalizeEntityKind(entityKind);
        if (string.IsNullOrEmpty(normalizedKind))
        {
            throw new ArgumentException(
                "Entity kind must contain usable characters after normalization.",
                nameof(entityKind));
        }

        string normalizedDomain = DomainName.Normalize(domain);
        string kindTag = CacheTags.EntityKind(normalizedDomain, normalizedKind);
        FootprintCacheBox<IReadOnlyList<T>?> box = await cache
            .GetOrCreateWithFootprintAsync<IReadOnlyList<T>>(
                new CacheEntryRequest
                {
                    Domain = normalizedDomain,
                    Key = logicalKey,
                    Footprint = EntityFootprint.Empty,
                    AdditionalTags = [kindTag]
                },
                async token =>
                {
                    EntitySet<T> produced = await factory(token).ConfigureAwait(false);
                    ArgumentNullException.ThrowIfNull(produced);
                    return new FootprintCacheBox<IReadOnlyList<T>?>
                    {
                        Value = produced.Value,
                        IsMiss = false,
                        Footprint = produced.BuildFootprint(normalizedKind)
                    };
                },
                cancellationToken)
            .ConfigureAwait(false);

        return box.Value ?? [];
    }

    private static void Validate<TFactory>(
        ICacheOrchestrator cache,
        string domain,
        string logicalKey,
        EntityRef primary,
        TFactory factory)
        where TFactory : Delegate
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        ArgumentNullException.ThrowIfNull(factory);

        string kind = DomainName.NormalizeEntityKind(primary.EntityKind);
        string id = DomainName.NormalizeResourceId(primary.ResourceId);
        if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id))
        {
            throw new ArgumentException(
                "Primary entity kind and id must contain usable characters after normalization.",
                nameof(primary));
        }
    }
}
