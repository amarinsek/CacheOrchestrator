using Microsoft.AspNetCore.Routing;

namespace CacheOrchestrator.HttpBus;

/// <summary>
/// Endpoint mapping for the HTTP cluster command bus.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Maps cluster receive endpoints when <c>Cache:Cluster:Bus:Enabled</c> is true.
    /// Independent of Local Admin. Safe no-op when bus is disabled.
    /// </summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapCacheOrchestratorHttpBus(this IEndpointRouteBuilder endpoints) =>
        ClusterLocalApi.Map(endpoints);
}
