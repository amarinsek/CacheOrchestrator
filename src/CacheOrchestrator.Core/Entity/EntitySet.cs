namespace CacheOrchestrator.Entity;

/// <summary>
/// Non-generic helpers for <see cref="EntitySet{T}"/>.
/// </summary>
public static class EntitySet
{
    /// <summary>
    /// Creates a set whose member kind is taken from the request / endpoint when cached.
    /// </summary>
    public static EntitySet<T> Create<T>(IEnumerable<T> items, Func<T, string> idSelector)
        => EntitySet<T>.Create(items, idSelector);

    /// <summary>
    /// Creates a set whose member kind is taken from the request / endpoint when cached.
    /// </summary>
    public static EntitySet<T> Create<T, TId>(IEnumerable<T> items, Func<T, TId> idSelector) where TId : notnull
        => EntitySet<T>.Create(items, idSelector);

    /// <summary>Creates a set with an explicit member entity kind.</summary>
    public static EntitySet<T> Create<T>(IEnumerable<T> items, string entityKind, Func<T, string> idSelector)
        => EntitySet<T>.Create(items, entityKind, idSelector);

    /// <summary>Creates a set with an explicit member entity kind.</summary>
    public static EntitySet<T> Create<T, TId>(IEnumerable<T> items, string entityKind, Func<T, TId> idSelector) where TId : notnull
        => EntitySet<T>.Create(items, entityKind, idSelector);
}

/// <summary>
/// Factory result for collection endpoints: values plus member / dependency footprint.
/// Lookup key is URL-shaped; tags include each member id.
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
public sealed class EntitySet<T>
{
    private readonly string? _memberKind;
    private readonly IReadOnlyList<string> _memberIds;
    private readonly EntityFootprint _extra;

    private EntitySet(
        IReadOnlyList<T> value,
        string? memberKind,
        IReadOnlyList<string> memberIds,
        EntityFootprint extra)
    {
        Value = value;
        _memberKind = memberKind;
        _memberIds = memberIds;
        _extra = extra ?? EntityFootprint.Empty;
    }

    /// <summary>Cached collection payload.</summary>
    public IReadOnlyList<T> Value { get; }

    /// <summary>
    /// Builds the full footprint using <paramref name="defaultMemberKind"/> when
    /// <see cref="Create(IEnumerable{T}, Func{T, string})"/> deferred the kind.
    /// </summary>
    public EntityFootprint BuildFootprint(string? defaultMemberKind)
    {
        string? kind = !string.IsNullOrWhiteSpace(_memberKind) ? _memberKind : defaultMemberKind;
        EntityFootprint members = EntityFootprint.Empty;
        if (!string.IsNullOrWhiteSpace(kind) && _memberIds.Count > 0)
        {
            List<EntityRef> refs = new(_memberIds.Count);
            for (int i = 0; i < _memberIds.Count; i++)
                refs.Add(new EntityRef(kind, _memberIds[i]));
            members = new EntityFootprint(primary: null, members: refs);
        }

        return members.Merge(_extra);
    }

    /// <summary>Creates a set; member kind comes from the request when cached.</summary>
    internal static EntitySet<T> Create(IEnumerable<T> items, Func<T, string> idSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(idSelector);
        (IReadOnlyList<T> list, IReadOnlyList<string> ids) = Materialize(items, idSelector);
        return new EntitySet<T>(list, memberKind: null, ids, EntityFootprint.Empty);
    }

    /// <summary>Creates a set; member kind comes from the request when cached.</summary>
    internal static EntitySet<T> Create<TId>(IEnumerable<T> items, Func<T, TId> idSelector) where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(idSelector);
        (IReadOnlyList<T> list, IReadOnlyList<string> ids) = Materialize(items, idSelector);
        return new EntitySet<T>(list, memberKind: null, ids, EntityFootprint.Empty);
    }

    /// <summary>Creates a set with an explicit member entity kind.</summary>
    internal static EntitySet<T> Create(IEnumerable<T> items, string entityKind, Func<T, string> idSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentNullException.ThrowIfNull(idSelector);
        (IReadOnlyList<T> list, IReadOnlyList<string> ids) = Materialize(items, idSelector);
        return new EntitySet<T>(list, entityKind.Trim(), ids, EntityFootprint.Empty);
    }

    /// <summary>Creates a set with an explicit member entity kind.</summary>
    internal static EntitySet<T> Create<TId>(IEnumerable<T> items, string entityKind, Func<T, TId> idSelector) where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentNullException.ThrowIfNull(idSelector);
        (IReadOnlyList<T> list, IReadOnlyList<string> ids) = Materialize(items, idSelector);
        return new EntitySet<T>(list, entityKind.Trim(), ids, EntityFootprint.Empty);
    }

    /// <summary>Adds a dependency shared by the whole set (e.g. filter category).</summary>
    public EntitySet<T> DependsOn(string entityKind, string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return new EntitySet<T>(
            Value,
            _memberKind,
            _memberIds,
            _extra.WithDependsOn([new EntityRef(entityKind, resourceId)]));
    }

    /// <summary>Adds a dependency shared by the whole set (e.g. filter category).</summary>
    public EntitySet<T> DependsOn<TId>(string entityKind, TId resourceId) where TId : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        return new EntitySet<T>(
            Value,
            _memberKind,
            _memberIds,
            _extra.WithDependsOn([new EntityRef(entityKind, FormatId(resourceId))]));
    }

    /// <summary>Adds per-row dependencies derived from each element.</summary>
    public EntitySet<T> DependsOn(Func<T, string> kindSelector, Func<T, string> idSelector)
    {
        ArgumentNullException.ThrowIfNull(kindSelector);
        ArgumentNullException.ThrowIfNull(idSelector);

        List<EntityRef> refs = [];
        for (int i = 0; i < Value.Count; i++)
        {
            T item = Value[i];
            string kind = kindSelector(item);
            string id = idSelector(item);
            if (!string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(id))
                refs.Add(new EntityRef(kind, id));
        }

        return new EntitySet<T>(Value, _memberKind, _memberIds, _extra.WithDependsOn(refs));
    }

    /// <summary>Adds per-row dependencies derived from each element.</summary>
    public EntitySet<T> DependsOn<TId>(Func<T, string> kindSelector, Func<T, TId> idSelector) where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(kindSelector);
        ArgumentNullException.ThrowIfNull(idSelector);

        List<EntityRef> refs = [];
        for (int i = 0; i < Value.Count; i++)
        {
            T item = Value[i];
            string kind = kindSelector(item);
            string id = FormatId(idSelector(item));
            if (!string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(id))
                refs.Add(new EntityRef(kind, id));
        }

        return new EntitySet<T>(Value, _memberKind, _memberIds, _extra.WithDependsOn(refs));
    }

    /// <summary>Adds an alias tag for the collection entry.</summary>
    public EntitySet<T> Alias(string entityKind, string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return new EntitySet<T>(
            Value,
            _memberKind,
            _memberIds,
            _extra.WithAliases([new EntityRef(entityKind, resourceId)]));
    }

    /// <summary>Adds an alias tag for the collection entry.</summary>
    public EntitySet<T> Alias<TId>(string entityKind, TId resourceId) where TId : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        return new EntitySet<T>(
            Value,
            _memberKind,
            _memberIds,
            _extra.WithAliases([new EntityRef(entityKind, FormatId(resourceId))]));
    }

    private static (IReadOnlyList<T> List, IReadOnlyList<string> Ids) Materialize(
        IEnumerable<T> items,
        Func<T, string> idSelector)
    {
        List<T> list = [];
        List<string> ids = [];
        foreach (T item in items)
        {
            list.Add(item);
            string id = idSelector(item);
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }

        return (list, ids);
    }

    private static (IReadOnlyList<T> List, IReadOnlyList<string> Ids) Materialize<TId>(
        IEnumerable<T> items,
        Func<T, TId> idSelector) where TId : notnull
    {
        List<T> list = [];
        List<string> ids = [];
        foreach (T item in items)
        {
            list.Add(item);
            string id = FormatId(idSelector(item));
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }

        return (list, ids);
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
