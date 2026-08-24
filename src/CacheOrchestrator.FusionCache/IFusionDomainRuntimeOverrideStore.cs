namespace CacheOrchestrator.FusionCache;

/// <summary>Process-local Fusion settings overlay store (Admin / cluster settings patches).</summary>
public interface IFusionDomainRuntimeOverrideStore
{
    /// <summary>Gets the overlay for <paramref name="domain"/>, or null when none.</summary>
    FusionDomainRuntimeOverride? Get(string domain);

    /// <summary>Monotonic stamp for <paramref name="domain"/> (0 when unset).</summary>
    int GetStamp(string domain);

    /// <summary>Merges <paramref name="patch"/> into the domain overlay.</summary>
    FusionDomainRuntimeOverride PatchSettings(string domain, FusionDomainSettingsPatch patch);

    /// <summary>Clears Fusion overlay for <paramref name="domain"/>.</summary>
    bool Clear(string domain);
}
