using CacheOrchestrator.FusionCache;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Consolidates CacheOrchestrator request state into a single allocation-free feature collection lookup.
/// </summary>
public interface ICacheOrchestratorFeature
{
    /// <summary>
    /// The resolved domain cache options for the current request.
    /// </summary>
    DomainCacheOptions? DomainOptions { get; set; }

    /// <summary>
    /// Normalized resource id.
    /// </summary>
    string? ResourceId { get; set; }

    /// <summary>
    /// Normalized entity kind.
    /// </summary>
    string? EntityKind { get; set; }

    /// <summary>
    /// Disposition indicating how the cache handled the request.
    /// </summary>
    CacheDisposition? Disposition { get; set; }

    /// <summary>
    /// Merged footprint of entities accessed during the request.
    /// </summary>
    EntityFootprint? PendingEntityFootprint { get; set; }
}

/// <summary>
/// Default implementation for <see cref="ICacheOrchestratorFeature"/>.
/// </summary>
internal sealed class CacheOrchestratorFeature : ICacheOrchestratorFeature
{
    public DomainCacheOptions? DomainOptions { get; set; }
    public string? ResourceId { get; set; }
    public string? EntityKind { get; set; }
    public CacheDisposition? Disposition { get; set; }
    public EntityFootprint? PendingEntityFootprint { get; set; }
}
