using CacheOrchestrator.HybridCache;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// DI registration for the Microsoft HybridCache <see cref="IDataCacheProvider"/>.
/// </summary>
public static class CacheOrchestratorHybridCacheServiceExtensions
{
    /// <summary>
    /// Registers <see cref="HybridDataCacheProvider"/> as the <see cref="IDataCacheProvider"/>,
    /// replacing any previously registered provider (e.g. Fusion from <c>AddCacheOrchestrator</c>).
    /// </summary>
    /// <remarks>
    /// Call <c>services.AddHybridCache()</c> before this method so
    /// <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> is available.
    /// Fusion-specific domain options (fail-safe, hard TTL, eager refresh, …) are ignored.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCacheOrchestratorHybridCache(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // AspNetCore AddCacheOrchestrator registers Fusion via TryAdd — replace for Hybrid topology.
        services.RemoveAll<IDataCacheProvider>();
        services.AddSingleton<IDataCacheProvider, HybridDataCacheProvider>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<CacheOrchestrator.Configuration.CacheOrchestratorOptions>, HybridCacheOptionsValidator>());
        return services;
    }
}
