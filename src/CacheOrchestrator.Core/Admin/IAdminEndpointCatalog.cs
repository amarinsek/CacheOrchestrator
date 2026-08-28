namespace CacheOrchestrator.Admin;

/// <summary>
/// Provides the host resource inventory exposed by the transport-independent management API.
/// ASP.NET Core supplies an endpoint-based implementation; non-HTTP hosts may register their own.
/// </summary>
public interface IAdminEndpointCatalog
{
    /// <summary>Returns discovered host resources and optional configured domains.</summary>
    IReadOnlyList<AdminEndpointInfoDto> GetEndpoints();
}
