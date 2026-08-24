namespace CacheOrchestrator.Admin;

/// <summary>
/// Process-local runtime overlays for domain Version / settings (Admin).
/// </summary>
public interface IDomainRuntimeOverrideStore
{
    /// <summary>Gets the overlay for a domain, or null.</summary>
    DomainRuntimeOverride? Get(string domain);

    /// <summary>Current stamp for a domain (0 if none).</summary>
    int GetStamp(string domain);

    /// <summary>Sets or replaces the Version overlay.</summary>
    DomainRuntimeOverride SetVersion(string domain, string version);

    /// <summary>Merges a partial settings patch into the domain overlay.</summary>
    DomainRuntimeOverride PatchSettings(string domain, DomainSettingsPatch patch);

    /// <summary>Clears all overlays for a domain.</summary>
    bool Clear(string domain);

    /// <summary>Domains that currently have any overlay.</summary>
    IReadOnlyCollection<string> GetOverriddenDomains();
}
