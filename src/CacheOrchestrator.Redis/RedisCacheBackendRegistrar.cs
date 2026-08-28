using CacheOrchestrator.Backends;
using CacheOrchestrator.FusionCache.Backends;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Meta registrar that implements both Output Cache and Fusion Redis surfaces.
/// Prefer <see cref="CacheOrchestratorRedisBuilderExtensions.AddRedisBackend"/> which registers
/// the same dual behaviour. Leaf packages expose
/// <see cref="RedisOutputCacheBackendRegistrar"/> and <see cref="RedisFusionCacheBackendRegistrar"/>.
/// </summary>
internal sealed class RedisCacheBackendRegistrar : IOutputCacheBackendRegistrar, IFusionCacheBackendRegistrar
{
    private readonly RedisOutputCacheBackendRegistrar _outputCache = new();
    private readonly RedisFusionCacheBackendRegistrar _fusionCache = new();

    /// <inheritdoc />
    public string Name => RedisConfiguration.ProviderName;

    /// <inheritdoc />
    public void RegisterOutputCache(OutputCacheRegistrationContext context) =>
        _outputCache.RegisterOutputCache(context);

    /// <inheritdoc />
    public void RegisterFusionCache(FusionCacheRegistrationContext context) =>
        _fusionCache.RegisterFusionCache(context);
}
