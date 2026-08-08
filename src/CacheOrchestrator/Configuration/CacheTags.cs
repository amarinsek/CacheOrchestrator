namespace CacheOrchestrator.Configuration;

/// <summary>
/// Well-known tag formats used for Output Cache and FusionCache invalidation.
/// </summary>
public static class CacheTags
{
    /// <summary>Prefix for domain-wide tags (<c>domain:{name}</c>).</summary>
    public const string DomainPrefix = "domain:";

    /// <summary>Prefix for per-entity tags (<c>entity:{domain}:{resourceId}</c>).</summary>
    public const string EntityPrefix = "entity:";

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
    /// <param name="normalizedResourceId">Already-normalized resource id.</param>
    public static string Entity(string normalizedDomain, string normalizedResourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedResourceId);
        return EntityPrefix + normalizedDomain + ":" + normalizedResourceId;
    }
}
