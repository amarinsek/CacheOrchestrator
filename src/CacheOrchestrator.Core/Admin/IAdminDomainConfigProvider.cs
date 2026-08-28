namespace CacheOrchestrator.Admin;

/// <summary>
/// Builds the effective domain view exposed by <see cref="ICacheOrchestratorManagement"/>.
/// Host packages can enrich the Core Data Cache view with transport-specific policy.
/// </summary>
public interface IAdminDomainConfigProvider
{
    /// <summary>Returns the effective configuration for a normalized domain name.</summary>
    AdminDomainConfigDto GetDomainConfig(string normalizedDomain);
}
