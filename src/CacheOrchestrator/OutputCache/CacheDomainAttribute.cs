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
    /// Optional route value name used as the entity id for Output Cache tags
    /// (e.g. <c>"id"</c> for <c>/api/products/{id}</c>). Enables fine-grained
    /// <see cref="Invalidation.ICacheOrchestratorInvalidator.InvalidateEntityAsync"/>.
    /// </summary>
    public string? ResourceRouteKey { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheDomainAttribute"/> class.
    /// </summary>
    /// <param name="domain">Non-empty cache domain name.</param>
    /// <param name="resourceRouteKey">Optional route value name for entity tagging.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="domain"/> is null, empty, or whitespace.</exception>
    public CacheDomainAttribute(string domain, string? resourceRouteKey = null)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain must not be null or empty.", nameof(domain));

        Domain = domain;
        ResourceRouteKey = string.IsNullOrWhiteSpace(resourceRouteKey) ? null : resourceRouteKey.Trim();
    }
}
