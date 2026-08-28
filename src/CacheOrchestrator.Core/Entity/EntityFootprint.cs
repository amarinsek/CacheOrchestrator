using CacheOrchestrator.Configuration;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace CacheOrchestrator.Entity;

/// <summary>
/// Invalidation footprint for a cached value: primary identity plus optional members,
/// dependencies, and aliases. Lookup keys are separate; this type only drives tags.
/// </summary>
/// <remarks>
/// Shape is System.Text.Json–friendly so HybridCache (and any STJ L2) can round-trip
/// <c>FootprintCacheBox{T}</c> that embeds this type.
/// </remarks>
public sealed class EntityFootprint
{
    private static readonly IReadOnlyList<EntityRef> EmptyRefs = [];

    /// <summary>Empty footprint (domain tag only once converted).</summary>
    public static EntityFootprint Empty { get; } = new(null, EmptyRefs, EmptyRefs, EmptyRefs);

    /// <summary>
    /// Creates a footprint with the given parts. Null/empty refs are dropped; duplicates are removed.
    /// </summary>
    public EntityFootprint(
        EntityRef? primary,
        IEnumerable<EntityRef>? members = null,
        IEnumerable<EntityRef>? dependsOn = null,
        IEnumerable<EntityRef>? aliases = null)
        : this(
            NormalizePrimary(primary),
            NormalizeList(members),
            NormalizeList(dependsOn),
            NormalizeList(aliases),
            alreadyNormalized: true)
    {
    }

    /// <summary>
    /// Deserialization constructor (property names match). Prefer the
    /// <see cref="EntityFootprint(EntityRef?, IEnumerable{EntityRef}?, IEnumerable{EntityRef}?, IEnumerable{EntityRef}?)"/>
    /// overload at call sites.
    /// </summary>
    [JsonConstructor]
    public EntityFootprint(
        EntityRef? primary,
        IReadOnlyList<EntityRef>? members,
        IReadOnlyList<EntityRef>? dependsOn,
        IReadOnlyList<EntityRef>? aliases)
        : this(primary, members, dependsOn, aliases, alreadyNormalized: false)
    {
    }

    private EntityFootprint(
        EntityRef? primary,
        IReadOnlyList<EntityRef>? members,
        IReadOnlyList<EntityRef>? dependsOn,
        IReadOnlyList<EntityRef>? aliases,
        bool alreadyNormalized)
    {
        Primary = alreadyNormalized ? primary : NormalizePrimary(primary);
        Members = alreadyNormalized
            ? members ?? EmptyRefs
            : NormalizeList(members);
        DependsOn = alreadyNormalized
            ? dependsOn ?? EmptyRefs
            : NormalizeList(dependsOn);
        Aliases = alreadyNormalized
            ? aliases ?? EmptyRefs
            : NormalizeList(aliases);
    }

    /// <summary>Primary entity for detail/aggregate entries, if any.</summary>
    public EntityRef? Primary { get; }

    /// <summary>Member entities (list rows, aggregate children).</summary>
    public IReadOnlyList<EntityRef> Members { get; }

    /// <summary>Related entities that should invalidate this entry when changed.</summary>
    public IReadOnlyList<EntityRef> DependsOn { get; }

    /// <summary>Alternate identity tags (e.g. SKU) for the same cached value.</summary>
    public IReadOnlyList<EntityRef> Aliases { get; }

    /// <summary>Returns a copy with <paramref name="primary"/> as the primary identity.</summary>
    public EntityFootprint WithPrimary(EntityRef primary)
    {
        EntityRef? normalized = NormalizePrimary(primary)
            ?? throw new ArgumentException("Primary entity kind and id must be usable after normalization.", nameof(primary));

        if (Primary == normalized)
            return this;

        return new EntityFootprint(normalized, Members, DependsOn, Aliases);
    }

    /// <summary>Returns a copy with additional member refs.</summary>
    public EntityFootprint WithMembers(IEnumerable<EntityRef> members)
        => new(Primary, Concat(Members, members), DependsOn, Aliases);

    /// <summary>Returns a copy with additional dependency refs.</summary>
    public EntityFootprint WithDependsOn(IEnumerable<EntityRef> dependsOn)
        => new(Primary, Members, Concat(DependsOn, dependsOn), Aliases);

    /// <summary>Returns a copy with additional alias refs.</summary>
    public EntityFootprint WithAliases(IEnumerable<EntityRef> aliases)
        => new(Primary, Members, DependsOn, Concat(Aliases, aliases));

    /// <summary>
    /// Builds Fusion / Output Cache tags for <paramref name="normalizedDomain"/>.
    /// Always includes <c>domain:{name}</c>; adds entity and entitykind tags for every ref.
    /// </summary>
    public IReadOnlyList<string> ToTags(string normalizedDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedDomain);

        List<string> tags = [CacheTags.Domain(normalizedDomain)];
        HashSet<string> kinds = new(StringComparer.Ordinal);
        HashSet<string> entities = new(StringComparer.Ordinal);

        void AddRef(EntityRef r)
        {
            string entityTag = CacheTags.Entity(normalizedDomain, r.EntityKind, r.ResourceId);
            if (entities.Add(entityTag))
                tags.Add(entityTag);

            if (kinds.Add(r.EntityKind))
                tags.Add(CacheTags.EntityKind(normalizedDomain, r.EntityKind));
        }

        if (Primary is { } primary)
            AddRef(primary);

        for (int i = 0; i < Members.Count; i++)
            AddRef(Members[i]);
        for (int i = 0; i < DependsOn.Count; i++)
            AddRef(DependsOn[i]);
        for (int i = 0; i < Aliases.Count; i++)
            AddRef(Aliases[i]);

        return tags;
    }

    /// <summary>
    /// Merges another footprint into this one (primary from <paramref name="other"/> wins when set).
    /// </summary>
    public EntityFootprint Merge(EntityFootprint? other)
    {
        if (other is null || ReferenceEquals(other, Empty) || ReferenceEquals(other, this))
            return this;

        return new EntityFootprint(
            other.Primary ?? Primary,
            Concat(Members, other.Members),
            Concat(DependsOn, other.DependsOn),
            Concat(Aliases, other.Aliases));
    }

    private static EntityRef? NormalizePrimary(EntityRef? primary)
    {
        if (primary is null)
            return null;

        string kind = DomainName.NormalizeEntityKind(primary.Value.EntityKind);
        string id = DomainName.NormalizeResourceId(primary.Value.ResourceId);
        if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id))
            return null;

        return new EntityRef(kind, id);
    }

    private static IReadOnlyList<EntityRef> NormalizeList(IEnumerable<EntityRef>? refs)
    {
        if (refs is null)
            return EmptyRefs;

        List<EntityRef> list = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (EntityRef r in refs)
        {
            string kind = DomainName.NormalizeEntityKind(r.EntityKind);
            string id = DomainName.NormalizeResourceId(r.ResourceId);
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id))
                continue;

            string key = kind + "\0" + id;
            if (!seen.Add(key))
                continue;

            list.Add(new EntityRef(kind, id));
        }

        return list.Count == 0
            ? EmptyRefs
            : new ReadOnlyCollection<EntityRef>(list);
    }

    private static IEnumerable<EntityRef> Concat(IReadOnlyList<EntityRef> left, IEnumerable<EntityRef>? right)
    {
        foreach (EntityRef r in left)
            yield return r;
        if (right is null)
            yield break;
        foreach (EntityRef r in right)
            yield return r;
    }
}
