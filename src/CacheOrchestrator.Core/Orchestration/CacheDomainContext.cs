using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Host-supplied cache domain binding for Http-free callers (libraries, workers).
/// </summary>
/// <remarks>
/// <para>
/// The application chooses the domain name (and optional entity kind) that maps to
/// <c>Cache:Domains:{Domain}</c>. Pass this into library APIs instead of hard-coding strings.
/// For HTTP Output Cache use the same <see cref="Domain"/> with
/// <c>CacheOutputWithDomain</c> / <c>[CacheDomain]</c> (or a per-request resolver for dynamic domains).
/// </para>
/// <para>
/// Does not carry resource ids or route field names — those stay on the method call / HTTP endpoint.
/// </para>
/// </remarks>
public sealed class CacheDomainContext
{
    /// <summary>
    /// Creates a context with a required domain and optional entity kind.
    /// </summary>
    /// <param name="domain">Cache domain name (normalized).</param>
    /// <param name="entityKind">Optional entity kind for entity/set APIs; normalized when set.</param>
    public CacheDomainContext(string domain, string? entityKind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        Domain = DomainName.Normalize(domain);
        EntityKind = string.IsNullOrWhiteSpace(entityKind)
            ? null
            : DomainName.NormalizeEntityKind(entityKind);
    }

    /// <summary>Normalized domain name (<c>Cache:Domains:{Domain}</c>).</summary>
    public string Domain { get; }

    /// <summary>
    /// Optional normalized entity kind for entity-scoped calls.
    /// When null, the library may apply its own default kind.
    /// </summary>
    public string? EntityKind { get; }

    /// <summary>
    /// Returns <see cref="EntityKind"/>, or <paramref name="defaultEntityKind"/> when unset.
    /// </summary>
    public string EntityKindOr(string defaultEntityKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultEntityKind);
        return EntityKind ?? DomainName.NormalizeEntityKind(defaultEntityKind);
    }
}
