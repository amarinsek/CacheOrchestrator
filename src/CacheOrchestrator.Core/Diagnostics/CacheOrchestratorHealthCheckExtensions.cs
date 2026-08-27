using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CacheOrchestrator.Diagnostics;

/// <summary>
/// Health check registration helpers for CacheOrchestrator.
/// </summary>
public static class CacheOrchestratorHealthCheckExtensions
{
    /// <summary>
    /// Adds a CacheOrchestrator health check that runs all registered <see cref="ICacheOrchestratorHealthProbe"/> instances.
    /// </summary>
    /// <param name="builder">Health checks builder.</param>
    /// <param name="name">Health check name.</param>
    /// <param name="failureStatus">Status reported when a probe fails.</param>
    /// <param name="timeout">Per-probe timeout (default 3 seconds).</param>
    /// <param name="tags">Optional health check tags.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddCacheOrchestrator(
        this IHealthChecksBuilder builder,
        string name = "cache_orchestrator",
        HealthStatus failureStatus = HealthStatus.Degraded,
        TimeSpan? timeout = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => ActivatorUtilities.CreateInstance<CacheOrchestratorHealthCheck>(sp),
            failureStatus,
            tags ?? ["cache", "ready"],
            timeout ?? TimeSpan.FromSeconds(3)));
    }
}
