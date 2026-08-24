using CacheOrchestrator.Entity;
using CacheOrchestrator.FusionCache;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Request-scoped CacheOrchestrator state on <see cref="HttpContext.Features"/>.
/// Prefer one feature lookup over multiple <c>HttpContext.Items</c> entries.
/// </summary>
/// <remarks>
/// Apps that previously read removed <c>CacheOrchestratorKeys</c> from <c>Items</c> should use
/// <see cref="Microsoft.AspNetCore.Http.CacheOrchestratorHttpContextExtensions.GetDomainCacheOptions"/>
/// or <c>http.Features.Get&lt;ICacheOrchestratorFeature&gt;()</c>. Mutate via
/// <see cref="FusionCache.IDomainFusionCache.SetEntityIdentity"/> / domain resolution APIs when possible;
/// installing a custom feature implementation is supported but uncommon.
/// </remarks>
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

/// <summary>
/// Gets or creates the request feature, always registered under <see cref="ICacheOrchestratorFeature"/>.
/// </summary>
internal static class CacheOrchestratorFeatureAccessor
{
    public static ICacheOrchestratorFeature GetOrCreate(HttpContext http)
    {
        ICacheOrchestratorFeature? feature = http.Features.Get<ICacheOrchestratorFeature>();
        if (feature is not null)
            return feature;

        feature = new CacheOrchestratorFeature();
        http.Features.Set<ICacheOrchestratorFeature>(feature);
        return feature;
    }
}
