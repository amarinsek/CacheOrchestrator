using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.DataCache;

/// <summary>
/// Generates deterministic cache keys for FusionCache based on the current HTTP request and domain configuration.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation is <see cref="DefaultDomainKeyGenerator"/>, which computes an XxHash3-based key
/// from the route pattern, route values, query string (tracking params stripped), Accept-Encoding, and host.
/// </para>
/// <para>
/// <strong>Custom key generator</strong>: implement this interface and replace the default registration before
/// <c>AddCacheOrchestrator</c> is called, or replace it after using <c>services.Replace</c>:
/// </para>
/// <code>
/// // Option A — replace before AddCacheOrchestrator:
/// services.AddSingleton&lt;IDomainKeyGenerator, MyTenantKeyGenerator&gt;();
/// services.AddCacheOrchestrator(configuration);
///
/// // Option B — replace after AddCacheOrchestrator (overrides the default):
/// services.AddCacheOrchestrator(configuration);
/// services.Replace(ServiceDescriptor.Singleton&lt;IDomainKeyGenerator, MyTenantKeyGenerator&gt;());
/// </code>
/// <para>
/// A typical custom generator extends <see cref="DefaultDomainKeyGenerator"/> by including additional
/// vary dimensions, for example the authenticated tenant ID or a custom header:
/// </para>
/// <code>
/// public sealed class TenantKeyGenerator : IDomainKeyGenerator
/// {
///     private readonly DefaultDomainKeyGenerator _inner = new();
///
///     public string Generate(
///         DomainHttpCacheOptions options,
///         HttpContext httpContext,
///         DomainCacheKeyShape shape = DomainCacheKeyShape.Automatic)
///     {
///         var baseKey = _inner.Generate(options, httpContext, shape);
///         var tenantId = httpContext.User.FindFirst("tenant_id")?.Value ?? "anon";
///         return $"{baseKey}|t:{tenantId}";
///     }
/// }
/// </code>
/// <para>
/// Keys must be stable (same inputs → same key), reasonably short, and not contain secrets,
/// as they are stored in Redis or memory and may appear in logs.
/// </para>
/// </remarks>
public interface IDomainKeyGenerator
{
    /// <summary>
    /// Creates a cache key for the given domain configuration and HTTP context.
    /// </summary>
    /// <param name="options">Resolved domain options.</param>
    /// <param name="httpContext">Current HTTP context.</param>
    /// <param name="shape">Whether entity identity should participate in the key.</param>
    /// <returns>A deterministic cache key string.</returns>
    string Generate(
        DomainHttpCacheOptions options,
        HttpContext httpContext,
        DomainCacheKeyShape shape = DomainCacheKeyShape.Automatic);
}

/// <summary>Controls whether a generated data-cache key uses request entity identity.</summary>
public enum DomainCacheKeyShape
{
    /// <summary>Use entity identity when both entity kind and resource id are present.</summary>
    Automatic = 0,

    /// <summary>Generate a URL-shaped key and ignore request entity identity.</summary>
    Url = 1,

    /// <summary>Generate an entity-shaped key when request entity identity is available.</summary>
    Entity = 2,
}
