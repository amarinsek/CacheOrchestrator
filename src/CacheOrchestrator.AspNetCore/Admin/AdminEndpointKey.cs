using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CacheOrchestrator.Admin;

/// <summary>Builds stable endpoint keys for Admin counters and optional metrics <c>route</c> tags.</summary>
internal static class AdminEndpointKey
{
    private static readonly object CacheSlot = new();

    /// <summary>
    /// Returns <c>METHOD pattern</c> for the current endpoint, or null when not a route endpoint.
    /// Cached per <see cref="HttpContext"/> so Admin stats + metrics share one resolution.
    /// </summary>
    public static string? TryGet(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (http.Items.TryGetValue(CacheSlot, out object? cached))
        {
            // Empty string = already resolved, no route endpoint.
            string? hit = cached as string;
            return string.IsNullOrEmpty(hit) ? null : hit;
        }

        string? key = Resolve(http);
        // Empty string = "resolved missing" so we do not recompute on later calls.
        http.Items[CacheSlot] = key ?? string.Empty;
        return key;
    }

    private static string? Resolve(HttpContext http)
    {
        if (http.GetEndpoint() is not RouteEndpoint route)
            return null;

        string pattern = route.RoutePattern.RawText ?? http.Request.Path.Value ?? string.Empty;
        if (pattern.Length == 0)
            return null;

        return string.Concat(http.Request.Method, " ", pattern);
    }
}
