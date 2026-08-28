using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Meta registration for Redis Output Cache + Fusion L2 / backplane.
/// </summary>
public static class CacheOrchestratorRedisBuilderExtensions
{
    /// <summary>
    /// Adds Redis for Output Cache and/or FusionCache instances (<c>"Provider": "Redis"</c>).
    /// </summary>
    /// <remarks>
    /// Registers both surfaces. For Output Cache only, use
    /// <see cref="CacheOrchestratorAspNetCoreRedisBuilderExtensions.AddRedisOutputCacheBackend"/>.
    /// For Fusion L2 only (no ASP.NET), use
    /// <see cref="CacheOrchestratorFusionCacheRedisServiceExtensions.AddRedisFusionCacheBackend"/>.
    /// </remarks>
    public static ICacheOrchestratorBuilder AddRedisBackend(
        this ICacheOrchestratorBuilder builder,
        string configSection = "Cache")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);

        builder.Services.AddSingleton<IValidateOptions<CacheOrchestratorOptions>>(
            new RedisProviderOptionsValidator(builder.Configuration, configSection));

        RedisCacheBackendRegistrar registrar = new();
        builder.AddOutputCacheBackend(registrar);
        FusionCacheBackendRegistrarRegistry.GetOrCreate(builder.Services).Add(registrar);
        return builder;
    }
}
