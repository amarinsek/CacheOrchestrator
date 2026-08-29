using CacheOrchestrator.Entity;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Request-scoped CacheOrchestrator state on <see cref="HttpContext.Features"/>.
/// Prefer one feature lookup over multiple <c>HttpContext.Items</c> entries.
/// </summary>
/// <remarks>
/// Apps that previously read removed <c>CacheOrchestratorKeys</c> from <c>Items</c> should use
/// <see cref="CacheOrchestratorHttpContextExtensions.GetDomainCacheOptions"/>
/// or <c>http.Features.Get&lt;ICacheOrchestratorFeature&gt;()</c>. Mutate via
/// <see cref="DataCache.IDomainDataCache.SetEntityIdentity"/> / domain resolution APIs when possible;
/// installing a custom feature implementation is supported but uncommon.
/// </remarks>
public interface ICacheOrchestratorFeature
{
    /// <summary>
    /// The resolved domain cache options for the current request.
    /// </summary>
    DomainHttpCacheOptions? DomainOptions { get; set; }

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
    public DomainHttpCacheOptions? DomainOptions { get; set; }
    public string? ResourceId { get; set; }
    public string? EntityKind { get; set; }
    public CacheDisposition? Disposition { get; set; }
    public EntityFootprint? PendingEntityFootprint { get; set; }
}

/// <summary>Internal cache-identity state kept separate from the replaceable public request feature.</summary>
internal sealed class CacheIdentityFeature
{
    public Identity.CacheIdentityMaterial? Material { get; set; }
    public bool Resolved { get; set; }
    public bool Bypass { get; set; }
}

/// <summary>Internal request execution state used to attribute factory work exactly once.</summary>
internal sealed class CacheFactoryExecutionFeature
{
    public string? DirectFactoryDomain { get; set; }
    public bool DataCacheObserved { get; set; }
    public bool DirectFactoryRecorded { get; set; }
    public long DirectFactoryStartedTimestamp { get; set; }
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
        http.Features.Set(feature);
        return feature;
    }
}

internal static class CacheIdentityFeatureAccessor
{
    public static CacheIdentityFeature GetOrCreate(HttpContext http)
    {
        CacheIdentityFeature? feature = http.Features.Get<CacheIdentityFeature>();
        if (feature is not null)
            return feature;

        feature = new CacheIdentityFeature();
        http.Features.Set(feature);
        return feature;
    }
}
internal static class CacheFactoryExecutionFeatureAccessor
{
    public static CacheFactoryExecutionFeature GetOrCreate(HttpContext http)
    {
        CacheFactoryExecutionFeature? feature = http.Features.Get<CacheFactoryExecutionFeature>();
        if (feature is not null)
            return feature;

        feature = new CacheFactoryExecutionFeature();
        http.Features.Set(feature);
        return feature;
    }
}
