namespace CacheOrchestrator.Admin;

/// <summary>
/// Process-local runtime overrides for domain Version and TTL values (Admin API).
/// Overlay is applied on top of bound configuration when building <see cref="Configuration.DomainCacheOptions"/>.
/// Values are not written to appsettings and are lost on process restart.
/// </summary>
public interface IDomainRuntimeOverrideStore
{
    /// <summary>Returns the current override for <paramref name="domain"/>, or null.</summary>
    DomainRuntimeOverride? Get(string domain);

    /// <summary>Monotonic stamp for the domain (0 if no override). Used to invalidate option snapshots.</summary>
    int GetStamp(string domain);

    /// <summary>
    /// Sets the runtime Version for the domain (bulk key cutover). Preserves existing TTL overrides.
    /// </summary>
    /// <param name="domain">Domain name (normalized internally).</param>
    /// <param name="version">Non-empty version token.</param>
    /// <returns>The updated override snapshot.</returns>
    DomainRuntimeOverride SetVersion(string domain, string version);

    /// <summary>
    /// Merges TTL fields into the domain override. Null properties in <paramref name="patch"/> leave prior values unchanged.
    /// </summary>
    DomainRuntimeOverride PatchTtl(string domain, DomainTtlPatch patch);

    /// <summary>Removes all runtime overrides for the domain.</summary>
    bool Clear(string domain);

    /// <summary>All domains that currently have any runtime override.</summary>
    IReadOnlyCollection<string> GetOverriddenDomains();
}
