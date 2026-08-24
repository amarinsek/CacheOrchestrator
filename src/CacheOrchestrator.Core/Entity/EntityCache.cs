namespace CacheOrchestrator.Entity;

/// <summary>
/// Non-generic helpers for <see cref="EntityCache{T}"/>.
/// </summary>
public static class EntityCache
{
    /// <summary>Creates a cacheable entity value with an empty extra footprint.</summary>
    public static EntityCache<T> Create<T>(T value) => EntityCache<T>.Create(value);

    /// <summary>
    /// Creates a negative-cache result (no value). Primary identity still comes from the request
    /// so create/update invalidation can purge the miss entry.
    /// </summary>
    public static EntityCache<T> Miss<T>() => EntityCache<T>.Miss();
}

/// <summary>
/// Factory result for entity get-or-set APIs (e.g. ASP.NET <c>IDomainDataCache.GetOrSetEntityAsync</c>)
/// that carries a value (or miss) plus optional footprint extensions.
/// </summary>
/// <typeparam name="T">Cached payload type.</typeparam>
public sealed class EntityCache<T>
{
    private EntityCache(T? value, bool isMiss, EntityFootprint footprint)
    {
        Value = value;
        IsMiss = isMiss;
        Footprint = footprint ?? EntityFootprint.Empty;
    }

    /// <summary>Cached value; <see langword="default"/> when <see cref="IsMiss"/> is <see langword="true"/>.</summary>
    public T? Value { get; }

    /// <summary>When <see langword="true"/>, the factory found no entity (negative cache).</summary>
    public bool IsMiss { get; }

    /// <summary>Extra invalidation refs (members / dependsOn / aliases). Primary is applied by the cache service.</summary>
    public EntityFootprint Footprint { get; }

    /// <summary>Creates a successful result.</summary>
    internal static EntityCache<T> Create(T value)
        => new(value, isMiss: false, EntityFootprint.Empty);

    /// <summary>Creates a miss / not-found result.</summary>
    internal static EntityCache<T> Miss()
        => new(default, isMiss: true, EntityFootprint.Empty);

    /// <summary>Adds member entity ids of the given kind.</summary>
    public EntityCache<T> Members(string entityKind, IEnumerable<string> resourceIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentNullException.ThrowIfNull(resourceIds);
        return WithFootprint(Footprint.WithMembers(ToRefs(entityKind, resourceIds)));
    }

    /// <summary>Adds member entity ids of the given kind.</summary>
    public EntityCache<T> Members<TId>(string entityKind, IEnumerable<TId> resourceIds) where TId : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentNullException.ThrowIfNull(resourceIds);
        return WithFootprint(Footprint.WithMembers(ToRefs(entityKind, resourceIds.Select(FormatId))));
    }

    /// <summary>Adds a single dependency.</summary>
    public EntityCache<T> DependsOn(string entityKind, string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return WithFootprint(Footprint.WithDependsOn([new EntityRef(entityKind, resourceId)]));
    }

    /// <summary>Adds a single dependency.</summary>
    public EntityCache<T> DependsOn<TId>(string entityKind, TId resourceId) where TId : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        return WithFootprint(Footprint.WithDependsOn([new EntityRef(entityKind, FormatId(resourceId))]));
    }

    /// <summary>Adds dependency ids of the given kind.</summary>
    public EntityCache<T> DependsOn(string entityKind, IEnumerable<string> resourceIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentNullException.ThrowIfNull(resourceIds);
        return WithFootprint(Footprint.WithDependsOn(ToRefs(entityKind, resourceIds)));
    }

    /// <summary>Adds dependency ids of the given kind.</summary>
    public EntityCache<T> DependsOn<TId>(string entityKind, IEnumerable<TId> resourceIds) where TId : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentNullException.ThrowIfNull(resourceIds);
        return WithFootprint(Footprint.WithDependsOn(ToRefs(entityKind, resourceIds.Select(FormatId))));
    }

    /// <summary>Adds an alternate identity tag (e.g. SKU).</summary>
    public EntityCache<T> Alias(string entityKind, string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return WithFootprint(Footprint.WithAliases([new EntityRef(entityKind, resourceId)]));
    }

    /// <summary>Adds an alternate identity tag (e.g. SKU).</summary>
    public EntityCache<T> Alias<TId>(string entityKind, TId resourceId) where TId : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        return WithFootprint(Footprint.WithAliases([new EntityRef(entityKind, FormatId(resourceId))]));
    }

    private EntityCache<T> WithFootprint(EntityFootprint footprint)
        => new(Value, IsMiss, footprint);

    private static IEnumerable<EntityRef> ToRefs(string entityKind, IEnumerable<string> resourceIds)
    {
        foreach (string id in resourceIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                yield return new EntityRef(entityKind, id);
        }
    }

    private static string FormatId<TId>(TId id) where TId : notnull
    {
        return id switch
        {
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => id.ToString() ?? string.Empty
        };
    }
}