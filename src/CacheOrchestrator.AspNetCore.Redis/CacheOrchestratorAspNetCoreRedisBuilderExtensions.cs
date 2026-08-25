using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Registers the Redis Output Cache backend with CacheOrchestrator.AspNetCore.
/// </summary>
public static class CacheOrchestratorAspNetCoreRedisBuilderExtensions
{
    /// <summary>
    /// Adds Redis as an Output Cache store provider (<c>"Provider": "Redis"</c> under <c>OutputCache</c>).
    /// </summary>
    public static ICacheOrchestratorBuilder AddRedisOutputCacheBackend(
        this ICacheOrchestratorBuilder builder,
        string configSection = "Cache")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);

        builder.Services.AddSingleton<IValidateOptions<CacheOrchestratorOptions>>(
            new RedisOutputCacheProviderOptionsValidator(builder.Configuration, configSection));
        builder.AddBackend(new RedisOutputCacheBackendRegistrar());
        return builder;
    }
}
