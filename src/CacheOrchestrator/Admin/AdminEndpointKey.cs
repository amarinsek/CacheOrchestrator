using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CacheOrchestrator.Admin;

/// <summary>Builds stable endpoint keys for Admin counters.</summary>
internal static class AdminEndpointKey
{
    /// <summary>
    /// Returns <c>METHOD pattern</c> for the current endpoint, or null when not a route endpoint.
    /// </summary>
    public static string? TryGet(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (http.GetEndpoint() is not RouteEndpoint route)
            return null;

        string pattern = route.RoutePattern.RawText ?? http.Request.Path.Value ?? string.Empty;
        if (pattern.Length == 0)
            return null;

        return string.Concat(http.Request.Method, " ", pattern);
    }
}
