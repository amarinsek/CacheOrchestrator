using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache.Backends;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Registers the Redis FusionCache L2 / backplane backend.
/// </summary>
public static class CacheOrchestratorFusionCacheRedisServiceExtensions
{
    /// <summary>
    /// Adds Redis as a Fusion data-cache provider (<c>"Provider": "Redis"</c> under <c>DataCacheInstances</c>).
    /// Does not require ASP.NET Core.
    /// </summary>
    public static IServiceCollection AddRedisFusionCacheBackend(
        this IServiceCollection services,
        IConfiguration configuration,
        string configSection = "Cache")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);

        services.AddSingleton<IValidateOptions<CacheOrchestratorOptions>>(
            new RedisFusionCacheProviderOptionsValidator(configuration, configSection));
        FusionCacheBackendRegistrarRegistry.GetOrCreate(services).Add(new RedisFusionCacheBackendRegistrar());
        return services;
    }
}
