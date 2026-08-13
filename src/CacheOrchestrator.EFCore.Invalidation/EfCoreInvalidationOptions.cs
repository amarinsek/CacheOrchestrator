namespace CacheOrchestrator.EFCore;

/// <summary>
/// Options for EF Core SaveChanges cache invalidation.
/// Operational flags bind from <c>Cache:EFCore:Invalidation</c>.
/// Type maps are code-only (<see cref="Map{TEntity}"/>) — not JSON.
/// </summary>
public sealed class EfCoreInvalidationOptions
{
    private readonly Dictionary<Type, EntityCacheMapping> _typeMaps = [];

    /// <summary>When false, the interceptor captures nothing and never calls the invalidator.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When a grouped change set has at least this many ids, <see cref="OnBulk"/> applies.
    /// </summary>
    public int BulkThreshold { get; set; } = 20;

    /// <summary>Bulk escape hatch. Default <see cref="EfCoreOnBulk.Kind"/>.</summary>
    public EfCoreOnBulk OnBulk { get; set; } = EfCoreOnBulk.Kind;

    /// <summary>
    /// Maps <typeparamref name="TEntity"/> to a cache domain and entity kind (composition-root catalog).
    /// Fluent model annotations and <see cref="CacheEntityAttribute"/> take precedence.
    /// </summary>
    public EfCoreInvalidationOptions Map<TEntity>(string domain, string entityKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        _typeMaps[typeof(TEntity)] = new EntityCacheMapping(domain.Trim(), entityKind.Trim());
        return this;
    }

    internal bool TryGetTypeMap(Type clrType, out EntityCacheMapping mapping) =>
        _typeMaps.TryGetValue(clrType, out mapping);
}
