using CacheOrchestrator.Invalidation;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Transport-independent management API for inspecting and operating a CacheOrchestrator instance.
/// HTTP, command-line, messaging, and other adapters should delegate to this contract.
/// </summary>
public interface ICacheOrchestratorManagement
{
    /// <summary>Returns local health and process information.</summary>
    Task<AdminHealthDto> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns local cluster identity, membership, and peers.</summary>
    Task<AdminClusterInfoDto> GetClusterInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns canonical raw process-lifetime diagnostic counters.</summary>
    AdminLiveStatsRawSnapshot GetStats();

    /// <summary>Returns resources discovered by the current host adapter.</summary>
    IReadOnlyList<AdminEndpointInfoDto> GetEndpoints();

    /// <summary>Returns effective configurations for all known domains.</summary>
    IReadOnlyList<AdminDomainConfigDto> GetDomains();

    /// <summary>Returns an effective configuration, or null when <paramref name="domain"/> is blank.</summary>
    AdminDomainConfigDto? GetDomain(string domain);

    /// <summary>Returns the catalog of domain settings and runtime overlay capabilities.</summary>
    AdminDomainSettingsCatalogDto GetDomainSettingsCatalog();

    /// <summary>Invalidates a domain, entity, entity kind, or explicit tags.</summary>
    Task<CacheInvalidationResult> InvalidateAsync(
        AdminInvalidateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sets or generates a domain version and optionally distributes it to peers.</summary>
    Task<AdminDomainMutationResultDto> SetVersionAsync(
        string domain,
        AdminVersionRequest? request = null,
        CancellationToken cancellationToken = default);

    /// <summary>Applies runtime domain settings and optionally distributes them to peers.</summary>
    Task<AdminDomainMutationResultDto> PatchSettingsAsync(
        string domain,
        AdminSettingsPatchRequest request,
        CancellationToken cancellationToken = default);
}
