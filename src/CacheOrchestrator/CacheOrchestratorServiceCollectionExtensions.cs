using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// Meta-package DI entry: ASP.NET host + FusionCache data provider.
/// </summary>
public static class CacheOrchestratorServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheOrchestrator ASP.NET services and the FusionCache data provider
    /// (named instances from <c>DataCacheInstances</c>).
    /// </summary>
    /// <remarks>
    /// Equivalent to <c>AddCacheOrchestratorAspNetCore</c> followed by
    /// <c>AddCacheOrchestratorFusionCache</c>. For HybridCache hosts, call
    /// <c>AddCacheOrchestratorAspNetCore</c> then <c>AddCacheOrchestratorHybridCache</c> instead.
    /// </remarks>
    public static IServiceCollection AddCacheOrchestrator(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ICacheOrchestratorBuilder>? configure = null,
        string configSection = "Cache",
        bool enableMvcConvention = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddCacheOrchestratorAspNetCore(
            configuration,
            configure,
            configSection,
            enableMvcConvention);
        services.AddCacheOrchestratorFusionCache(configuration, configSection);
        return services;
    }
}
