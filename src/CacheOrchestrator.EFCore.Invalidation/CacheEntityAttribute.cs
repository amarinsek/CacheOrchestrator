namespace CacheOrchestrator.EFCore;

/// <summary>
/// Maps a CLR entity type to a CacheOrchestrator domain and entity kind for SaveChanges invalidation.
/// </summary>
/// <remarks>
/// Fluent <c>EntityTypeBuilder.CacheInvalidate</c> takes precedence when both are present.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CacheEntityAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheEntityAttribute"/> class.
    /// </summary>
    /// <param name="domain">Cache domain (policy group).</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    public CacheEntityAttribute(string domain, string entityKind)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain must not be null or empty.", nameof(domain));
        if (string.IsNullOrWhiteSpace(entityKind))
            throw new ArgumentException("Entity kind must not be null or empty.", nameof(entityKind));

        Domain = domain.Trim();
        EntityKind = entityKind.Trim();
    }

    /// <summary>Cache domain name (policy group).</summary>
    public string Domain { get; }

    /// <summary>Resource type within the domain.</summary>
    public string EntityKind { get; }
}
