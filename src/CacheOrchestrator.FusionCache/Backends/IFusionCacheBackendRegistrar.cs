namespace CacheOrchestrator.FusionCache.Backends;

/// <summary>
/// Registers distributed cache / backplane for one named FusionCache instance.
/// </summary>
public interface IFusionCacheBackendRegistrar
{
    /// <summary>Backend name. Must match <c>DataCacheInstances:{name}:Provider</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Registers L2 / backplane for <paramref name="context"/>.InstanceName.
    /// </summary>
    void RegisterFusionCache(FusionCacheRegistrationContext context);

    /// <summary>
    /// Optional health probes for this Fusion instance.
    /// </summary>
    void RegisterHealthProbes(FusionBackendHealthRegistrationContext context);
}
