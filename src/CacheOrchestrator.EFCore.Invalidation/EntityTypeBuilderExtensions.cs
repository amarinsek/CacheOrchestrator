using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CacheOrchestrator.EFCore;

/// <summary>
/// Fluent mapping of an EF entity type to a CacheOrchestrator domain and entity kind.
/// </summary>
public static class EntityTypeBuilderExtensions
{
    /// <summary>
    /// Marks this entity type for SaveChanges cache invalidation.
    /// Takes precedence over <see cref="CacheEntityAttribute"/> and <see cref="EfCoreInvalidationOptions.Map{TEntity}"/>.
    /// </summary>
    public static EntityTypeBuilder<TEntity> CacheInvalidate<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string domain,
        string entityKind)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        Apply(builder, domain, entityKind);
        return builder;
    }

    /// <summary>
    /// Marks this entity type for SaveChanges cache invalidation.
    /// </summary>
    public static EntityTypeBuilder CacheInvalidate(
        this EntityTypeBuilder builder,
        string domain,
        string entityKind)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Apply(builder, domain, entityKind);
        return builder;
    }

    private static void Apply(EntityTypeBuilder builder, string domain, string entityKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        builder.HasAnnotation(CacheOrchestratorEfAnnotations.Domain, domain.Trim());
        builder.HasAnnotation(CacheOrchestratorEfAnnotations.EntityKind, entityKind.Trim());
    }
}
