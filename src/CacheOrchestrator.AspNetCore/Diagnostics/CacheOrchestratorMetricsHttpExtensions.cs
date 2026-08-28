using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Diagnostics;

/// <summary>
/// HTTP helpers for <see cref="CacheOrchestratorMetrics"/> endpoint labeling.
/// </summary>
public static class CacheOrchestratorMetricsHttpExtensions
{
    /// <summary>
    /// When <c>Cache:Metrics:IncludeEndpointLabel</c> is true, returns the stable endpoint key
    /// (<c>METHOD pattern</c>, same as Admin). Otherwise null without building a key.
    /// Uses <see cref="AdminEndpointKey.TryGet"/> (per-request cache).
    /// </summary>
    public static string? TryGetEndpointRouteLabel(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!IsEndpointLabelEnabled(http))
            return null;
        string? key = AdminEndpointKey.TryGet(http);
        return string.IsNullOrEmpty(key) ? null : key;
    }

    /// <summary>
    /// Resolves Admin endpoint key and optional metrics route in one pass.
    /// </summary>
    /// <param name="http">Current HTTP context.</param>
    /// <param name="forAdminStats">When true, always resolve the key for Local Admin counters.</param>
    /// <param name="forMetrics">When true, resolve the route label if endpoint labeling is enabled.</param>
    /// <param name="endpointKey">Admin counter key when <paramref name="forAdminStats"/> is true.</param>
    /// <param name="metricsRoute">Route tag when IncludeEndpointLabel is enabled.</param>
    internal static void ResolveEndpointKeys(
        HttpContext http,
        bool forAdminStats,
        bool forMetrics,
        out string? endpointKey,
        out string? metricsRoute)
    {
        endpointKey = null;
        metricsRoute = null;
        bool includeRoute = forMetrics && IsEndpointLabelEnabled(http);
        if (!forAdminStats && !includeRoute)
            return;

        string? key = AdminEndpointKey.TryGet(http);
        if (string.IsNullOrEmpty(key))
            return;

        if (forAdminStats)
            endpointKey = key;
        if (includeRoute)
            metricsRoute = key;
    }

    private static bool IsEndpointLabelEnabled(HttpContext http) =>
        http.RequestServices?.GetService<IOptions<CacheOrchestratorHttpOptions>>()?.Value
            is { Metrics.IncludeEndpointLabel: true };
}
