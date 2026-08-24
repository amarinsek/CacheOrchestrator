using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Registers the Redis backend with CacheOrchestrator.
/// </summary>
public static class CacheOrchestratorRedisBuilderExtensions
{
    /// <summary>
    /// Adds the Redis storage provider so configuration can use <c>"Provider": "Redis"</c>
    /// for Output Cache and/or FusionCache instances.
    /// </summary>
    /// <param name="builder">The CacheOrchestrator builder from <c>AddCacheOrchestrator</c>.</param>
    /// <param name="configSection">
    /// Configuration section name (must match the section passed to <c>AddCacheOrchestrator</c>).
    /// Default: <c>Cache</c>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <code>
    /// services.AddCacheOrchestrator(configuration, o => o.AddRedisBackend());
    /// services.AddCacheOrchestratorFusionCache(configuration);
    /// // custom section:
    /// services.AddCacheOrchestrator(configuration, o => o.AddRedisBackend("MyCache"), configSection: "MyCache");
    /// </code>
    /// Registers connection validation for Redis providers and the <see cref="RedisCacheBackendRegistrar"/>
    /// for both Output Cache and Fusion L2. Redis settings are read from <c>{section}:Redis</c>
    /// and optional per-provider overrides — not from core <c>CacheOrchestratorOptions</c>.
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
        builder.AddBackend(registrar);
        FusionCacheBackendRegistrarRegistry.GetOrCreate(builder.Services).Add(registrar);
        return builder;
    }
}
