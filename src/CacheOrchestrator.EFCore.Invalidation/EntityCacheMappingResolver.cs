using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace CacheOrchestrator.EFCore;

/// <summary>
/// Fluent EF annotations first, then <see cref="CacheEntityAttribute"/>, then code <see cref="EfCoreInvalidationOptions.Map{TEntity}"/>.
/// </summary>
internal sealed class EntityCacheMappingResolver : IEntityCacheMappingResolver
{
    private readonly IOptionsMonitor<EfCoreInvalidationOptions> _options;

    public EntityCacheMappingResolver(IOptionsMonitor<EfCoreInvalidationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public bool TryResolve(IReadOnlyEntityType entityType, out EntityCacheMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        if (TryResolveFromAnnotations(entityType, out mapping))
            return true;

        Type clrType = entityType.ClrType;

        CacheEntityAttribute? attr = clrType.GetCustomAttribute<CacheEntityAttribute>(inherit: true);
        if (attr is not null)
        {
            mapping = new EntityCacheMapping(attr.Domain, attr.EntityKind);
            return true;
        }

        return _options.CurrentValue.TryGetTypeMap(clrType, out mapping);
    }

    private static bool TryResolveFromAnnotations(IReadOnlyEntityType entityType, out EntityCacheMapping mapping)
    {
        mapping = default;
        string? domain = entityType.FindAnnotation(CacheOrchestratorEfAnnotations.Domain)?.Value as string;
        string? kind = entityType.FindAnnotation(CacheOrchestratorEfAnnotations.EntityKind)?.Value as string;
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(kind))
            return false;

        mapping = new EntityCacheMapping(domain.Trim(), kind.Trim());
        return true;
    }
}
