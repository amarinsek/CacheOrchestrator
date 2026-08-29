using CacheOrchestrator.Admin;
using CacheOrchestrator.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// ASP.NET Core pipeline extensions for CacheOrchestrator.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the necessary middleware for CacheOrchestrator.
    /// Call this after <c>UseRouting()</c> and before Map* endpoints.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseCacheOrchestrator(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Output Cache must run before endpoints
        app.UseOutputCache();

        // Runs only after an Output Cache miss/bypass/off reaches the application pipeline.
        app.UseMiddleware<DirectFactoryTelemetryMiddleware>();

        return app;
    }

    /// <summary>
    /// Maps Local Admin API routes when <c>Cache:Admin:Enabled</c> is true; otherwise a no-op.
    /// Call after routing is configured (typically next to other <c>Map*</c> calls).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapCacheOrchestratorAdmin(this IEndpointRouteBuilder endpoints) =>
        AdminLocalApi.Map(endpoints);
}
