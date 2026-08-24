namespace CacheOrchestrator.Configuration;

/// <summary>
/// Well-known tag formats used for Output Cache and data-cache invalidation.
/// </summary>
public static class CacheTags
{
    /// <summary>Prefix for domain-wide tags (<c>domain:{name}</c>).</summary>
    public const string DomainPrefix = "domain:";

    /// <summary>Prefix for per-entity tags (<c>entity:{domain}:{entityKind}:{resourceId}</c>).</summary>
    public const string EntityPrefix = "entity:";

    /// <summary>Prefix for kind-wide tags (<c>entitykind:{domain}:{entityKind}</c>).</summary>
    public const string EntityKindPrefix = "entitykind:";

    /// <summary>
    /// Builds the domain tag used on every entry for a cache domain.
    /// </summary>
    /// <param name="normalizedDomain">Already-normalized domain name.</param>
    public static string Domain(string normalizedDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedDomain);
        return DomainPrefix + normalizedDomain;
    }

    /// <summary>
    /// Builds the entity tag for a single resource inside a domain.
    /// </summary>
    /// <param name="normalizedDomain">Already-normalized domain name.</param>
    /// <param name="normalizedEntityKind">Already-normalized entity kind (resource type).</param>
    /// <param name="normalizedResourceId">Already-normalized resource id.</param>
    public static string Entity(string normalizedDomain, string normalizedEntityKind, string normalizedResourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEntityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedResourceId);
        return EntityPrefix + normalizedDomain + ":" + normalizedEntityKind + ":" + normalizedResourceId;
    }

    /// <summary>
    /// Builds the kind-wide tag for every entry of a given entity kind inside a domain.
    /// </summary>
    /// <param name="normalizedDomain">Already-normalized domain name.</param>
    /// <param name="normalizedEntityKind">Already-normalized entity kind (resource type).</param>
    public static string EntityKind(string normalizedDomain, string normalizedEntityKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEntityKind);
        return EntityKindPrefix + normalizedDomain + ":" + normalizedEntityKind;
    }
}
