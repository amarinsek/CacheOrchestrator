using Microsoft.EntityFrameworkCore.Metadata;

namespace CacheOrchestrator.EFCore;

/// <summary>Resolves an EF entity type to a cache domain and entity kind.</summary>
internal interface IEntityCacheMappingResolver
{
    /// <summary>
    /// Fluent annotations, then <see cref="CacheEntityAttribute"/>, then <see cref="EfCoreInvalidationOptions.Map{TEntity}"/>.
    /// </summary>
    bool TryResolve(IReadOnlyEntityType entityType, out EntityCacheMapping mapping);
}
