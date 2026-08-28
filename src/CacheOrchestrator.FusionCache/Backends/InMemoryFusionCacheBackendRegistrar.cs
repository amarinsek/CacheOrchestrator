namespace CacheOrchestrator.FusionCache.Backends;

/// <summary>
/// No-op Fusion L2 registrar (L1 memory only).
/// </summary>
internal sealed class InMemoryFusionCacheBackendRegistrar : IFusionCacheBackendRegistrar
{
    /// <inheritdoc />
    public string Name => "InMemory";

    /// <inheritdoc />
    public void RegisterFusionCache(FusionCacheRegistrationContext context)
    {
        // FusionCache already uses the registered IMemoryCache as L1.
        // No L2 or backplane for InMemory.
    }

}
