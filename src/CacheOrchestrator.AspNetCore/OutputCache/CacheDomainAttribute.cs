namespace CacheOrchestrator.OutputCache;

/// <summary>
/// Marks a controller, action, or endpoint with a cache domain so Output Caching uses that domain's configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CacheDomainAttribute : Attribute
{
    /// <summary>
    /// Cache domain name as configured under <c>Cache:Domains</c> (normalized later by the policy).
    /// </summary>
    public string Domain { get; }

    /// <summary>
    /// Route value name used as the entity id for Output Cache tags
    /// (e.g. <c>"id"</c> for <c>/api/products/{id}</c>). Requires <see cref="EntityKind"/>.
    /// </summary>
    public string? ResourceRouteKey { get; }

    /// <summary>
    /// Resource type within the domain (e.g. <c>products</c>).
    /// With <see cref="ResourceRouteKey"/>: primary entity tags.
    /// Alone: kind-scoped / list tags (<c>entitykind:{domain}:{entityKind}</c>).
    /// </summary>
    public string? EntityKind { get; }

    /// <summary>
    /// Binds a domain without entity tagging (snapshot endpoints).
    /// </summary>
    /// <param name="domain">Non-empty cache domain name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="domain"/> is null, empty, or whitespace.</exception>
    public CacheDomainAttribute(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain must not be null or empty.", nameof(domain));

        Domain = domain;
    }

    /// <summary>
    /// Binds a domain and entity kind for collection / kind-scoped endpoints (no single resource id).
    /// </summary>
    /// <param name="domain">Non-empty cache domain name.</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    /// <exception cref="ArgumentException">Thrown when any argument is null, empty, or whitespace.</exception>
    public CacheDomainAttribute(string domain, string entityKind)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain must not be null or empty.", nameof(domain));
        if (string.IsNullOrWhiteSpace(entityKind))
            throw new ArgumentException("Entity kind must not be null or empty.", nameof(entityKind));

        Domain = domain;
        EntityKind = entityKind.Trim();
    }

    /// <summary>
    /// Binds a domain and entity identity from a route value.
    /// </summary>
    /// <param name="domain">Non-empty cache domain name.</param>
    /// <param name="resourceRouteKey">Route value name for the resource id (e.g. <c>"id"</c>).</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    /// <exception cref="ArgumentException">Thrown when any argument is null, empty, or whitespace.</exception>
    public CacheDomainAttribute(string domain, string resourceRouteKey, string entityKind)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain must not be null or empty.", nameof(domain));
        if (string.IsNullOrWhiteSpace(resourceRouteKey))
            throw new ArgumentException("Resource route key must not be null or empty.", nameof(resourceRouteKey));
        if (string.IsNullOrWhiteSpace(entityKind))
            throw new ArgumentException("Entity kind must not be null or empty.", nameof(entityKind));

        Domain = domain;
        ResourceRouteKey = resourceRouteKey.Trim();
        EntityKind = entityKind.Trim();
    }
}
