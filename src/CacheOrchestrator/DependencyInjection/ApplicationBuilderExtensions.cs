using Microsoft.AspNetCore.Builder;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// ASP.NET Core pipeline extensions for CacheOrchestrator.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the necessary middleware for CacheOrchestrator (currently Output Cache).
    /// Call this after <c>UseRouting()</c> and before Map* endpoints.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseCacheOrchestrator(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Output Cache must run before endpoints
        app.UseOutputCache();

        // Future: custom middleware (early domain config resolution, global X-Cache headers, etc.)

        return app;
    }
}