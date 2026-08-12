namespace CacheOrchestrator.Admin;

/// <summary>
/// Discovers application endpoints and their configured cache domains for Local Admin API.
/// </summary>
public interface IAdminEndpointCatalog
{
    /// <summary>Enumerates current route endpoints and optional fixed domain metadata.</summary>
    IReadOnlyList<AdminEndpointInfoDto> GetEndpoints();
}
