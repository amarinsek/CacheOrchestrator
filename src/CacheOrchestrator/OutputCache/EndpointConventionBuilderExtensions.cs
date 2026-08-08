using CacheOrchestrator.Utilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.OutputCache;

/// <summary>
/// Extension methods that attach <see cref="DomainOutputCachePolicy"/> to Minimal API / endpoint routes.
/// </summary>
public static class EndpointConventionBuilderExtensions
{
    /// <summary>
    /// Binds a fixed cache domain to this endpoint's output cache policy.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="domain">Cache domain name from configuration.</param>
    /// <param name="resourceRouteKey">
    /// Optional route value name for entity Output Cache tags (e.g. <c>"id"</c>).
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder CacheOutputWithDomain(
        this RouteHandlerBuilder builder,
        string domain,
        string? resourceRouteKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new DomainOutputCachePolicy(domain, resourceRouteKey));
    }

    /// <summary>
    /// Binds a per-request domain resolver to this endpoint's output cache policy.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="domainResolver">Delegate that returns the domain for the current request.</param>
    /// <param name="resourceRouteKey">Optional route value name for entity Output Cache tags.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder CacheOutputWithDomain(
        this RouteHandlerBuilder builder,
        Func<HttpContext, string> domainResolver,
        string? resourceRouteKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new DomainOutputCachePolicy(domainResolver, resourceRouteKey));
    }

    /// <summary>
    /// Resolves the domain from a template (e.g. <c>tenant-{host}-{route:id}</c>) and binds the resulting policy.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="template">Domain template string (see <see cref="DomainTemplateCompiler"/>).</param>
    /// <param name="customProviders">Optional custom token providers for <c>{custom:key}</c> placeholders.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder CacheOutputWithDomainTemplate(
        this RouteHandlerBuilder builder,
        string template,
        IReadOnlyDictionary<string, Func<HttpContext, string?>>? customProviders = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Func<HttpContext, string> compiled = DomainTemplateCompiler.GetOrAdd(template, customProviders);
        return builder.CacheOutputWithDomain(compiled);
    }

    /// <summary>
    /// Applies output caching from <see cref="CacheDomainAttribute"/> metadata when present on the endpoint.
    /// </summary>
    /// <typeparam name="TBuilder">Endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TBuilder CacheOutputWithDomainAttribute<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpointBuilder =>
        {
            CacheDomainAttribute? attribute = endpointBuilder.Metadata
                .OfType<CacheDomainAttribute>()
                .LastOrDefault();

            if (attribute is not null)
                endpointBuilder.Metadata.Add(new DomainOutputCachePolicy(attribute.Domain, attribute.ResourceRouteKey));
        });

        return builder;
    }
}