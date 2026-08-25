using CacheOrchestrator.Backends;
using CacheOrchestrator.FusionCache.Backends;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Meta registrar that implements both Output Cache and Fusion Redis surfaces.
/// Prefer <see cref="CacheOrchestratorRedisBuilderExtensions.AddRedisBackend"/> which registers
/// the same dual behaviour. Leaf packages expose
/// <see cref="RedisOutputCacheBackendRegistrar"/> and <see cref="RedisFusionCacheBackendRegistrar"/>.
/// </summary>
public sealed class RedisCacheBackendRegistrar : ICacheBackendRegistrar, IFusionCacheBackendRegistrar
{
    private readonly RedisOutputCacheBackendRegistrar _outputCache = new();
    private readonly RedisFusionCacheBackendRegistrar _fusionCache = new();

    /// <inheritdoc />
    public string Name => RedisConfiguration.ProviderName;

    /// <inheritdoc />
    public bool SupportsOutputCacheStore => true;

    /// <inheritdoc />
    public void RegisterOutputCache(OutputCacheRegistrationContext context) =>
        _outputCache.RegisterOutputCache(context);

    /// <inheritdoc />
    public void RegisterFusionCache(FusionCacheRegistrationContext context) =>
        _fusionCache.RegisterFusionCache(context);

    /// <inheritdoc cref="ICacheBackendRegistrar.RegisterHealthProbes" />
    public void RegisterHealthProbes(BackendHealthRegistrationContext context) =>
        _outputCache.RegisterHealthProbes(context);

    /// <inheritdoc cref="IFusionCacheBackendRegistrar.RegisterHealthProbes" />
    public void RegisterHealthProbes(FusionBackendHealthRegistrationContext context) =>
        _fusionCache.RegisterHealthProbes(context);
}
