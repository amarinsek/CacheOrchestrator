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
    /// Binds a fixed cache domain to this endpoint's output cache policy (domain tag only).
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="domain">Cache domain name from configuration.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder CacheOutputWithDomain(
        this RouteHandlerBuilder builder,
        string domain)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new DomainOutputCachePolicy(domain));
    }

    /// <summary>
    /// Binds a fixed cache domain and entity identity (route value + entity kind).
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="domain">Cache domain name from configuration.</param>
    /// <param name="resourceRouteKey">Route value name that holds the id (e.g. <c>"id"</c>).</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder CacheOutputWithDomain(
        this RouteHandlerBuilder builder,
        string domain,
        string resourceRouteKey,
        string entityKind)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new DomainOutputCachePolicy(domain, resourceRouteKey, entityKind));
    }

    /// <summary>
    /// Binds a per-request domain resolver to this endpoint's output cache policy (domain tag only).
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="domainResolver">Delegate that returns the domain for the current request.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder CacheOutputWithDomain(
        this RouteHandlerBuilder builder,
        Func<HttpContext, string> domainResolver)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new DomainOutputCachePolicy(domainResolver));
    }

    /// <summary>
    /// Binds a per-request domain resolver with entity identity (route value + entity kind).
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="domainResolver">Delegate that returns the domain for the current request.</param>
    /// <param name="resourceRouteKey">Route value name that holds the id (e.g. <c>"id"</c>).</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder CacheOutputWithDomain(
        this RouteHandlerBuilder builder,
        Func<HttpContext, string> domainResolver,
        string resourceRouteKey,
        string entityKind)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new DomainOutputCachePolicy(domainResolver, resourceRouteKey, entityKind));
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

            if (attribute is null)
                return;

            endpointBuilder.Metadata.Add(CreatePolicy(attribute));
        });

        return builder;
    }

    internal static DomainOutputCachePolicy CreatePolicy(CacheDomainAttribute attribute)
    {
        if (attribute.ResourceRouteKey is not null && attribute.EntityKind is not null)
            return new DomainOutputCachePolicy(attribute.Domain, attribute.ResourceRouteKey, attribute.EntityKind);

        return new DomainOutputCachePolicy(attribute.Domain);
    }
}
