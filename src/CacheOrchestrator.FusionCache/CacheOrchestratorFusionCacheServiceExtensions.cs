using CacheOrchestrator.FusionCache;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// DI registration for the FusionCache <see cref="IDataCacheProvider"/>.
/// </summary>
public static class CacheOrchestratorFusionCacheServiceExtensions
{
    /// <summary>
    /// Registers <see cref="FusionDataCacheProvider"/> as the default <see cref="IDataCacheProvider"/>.
    /// Named FusionCache instances must still be registered by the host (e.g. via AspNetCore <c>AddCacheOrchestrator</c>).
    /// Fusion-specific knobs bind from <c>DataCache</c> on each domain (see <c>DomainDataCacheSettings</c>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configSection">Root configuration section (default <c>Cache</c>); retained for call-site compatibility.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCacheOrchestratorFusionCache(
        this IServiceCollection services,
        string configSection = "Cache")
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = configSection;

        services.TryAddSingleton<IDataCacheProvider, FusionDataCacheProvider>();
        return services;
    }
}
