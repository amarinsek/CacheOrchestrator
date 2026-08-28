using CacheOrchestrator.Configuration;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Discovers <see cref="RouteEndpoint"/>s from ASP.NET Core <see cref="EndpointDataSource"/>s.
/// </summary>
internal sealed class AdminEndpointCatalog : IAdminEndpointCatalog
{
    private readonly IEnumerable<EndpointDataSource> _dataSources;

    public AdminEndpointCatalog(IEnumerable<EndpointDataSource> dataSources)
    {
        ArgumentNullException.ThrowIfNull(dataSources);
        _dataSources = dataSources;
    }

    /// <inheritdoc />
    public IReadOnlyList<AdminEndpointInfoDto> GetEndpoints()
    {
        List<AdminEndpointInfoDto> list = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (EndpointDataSource source in _dataSources)
        {
            foreach (Endpoint endpoint in source.Endpoints)
            {
                if (endpoint is not RouteEndpoint route)
                    continue;

                string pattern = route.RoutePattern.RawText ?? string.Empty;
                if (pattern.Length == 0)
                    continue;

                // Prefer HTTP method metadata; fall back to enumerating all verbs as "*".
                IReadOnlyList<string> methods = GetHttpMethods(route);
                string? configuredDomain = ResolveConfiguredDomain(route);
                string? displayName = ResolveDisplayName(route);

                foreach (string method in methods)
                {
                    string key = string.Concat(method, " ", pattern);
                    if (!seen.Add(key))
                        continue;

                    list.Add(new AdminEndpointInfoDto
                    {
                        Route = key,
                        Method = method,
                        Pattern = pattern,
                        ConfiguredDomain = configuredDomain,
                        DisplayName = displayName
                    });
                }
            }
        }

        list.Sort(static (a, b) => string.CompareOrdinal(a.Route, b.Route));
        return list;
    }

    private static IReadOnlyList<string> GetHttpMethods(RouteEndpoint route)
    {
        HttpMethodMetadata? meta = route.Metadata.GetMetadata<HttpMethodMetadata>();
        if (meta?.HttpMethods is { Count: > 0 } methods)
            return [.. methods];

        return ["*"];
    }

    private static string? ResolveConfiguredDomain(RouteEndpoint route)
    {
        DomainOutputCachePolicy? policy = route.Metadata.OfType<DomainOutputCachePolicy>().LastOrDefault();
        if (policy?.FixedDomain is { Length: > 0 } fixedDomain)
            return fixedDomain;

        CacheDomainAttribute? attr = route.Metadata.OfType<CacheDomainAttribute>().LastOrDefault();
        if (attr is not null && !string.IsNullOrWhiteSpace(attr.Domain))
            return DomainName.Normalize(attr.Domain);

        return null;
    }

    private static string? ResolveDisplayName(RouteEndpoint route)
    {
        ControllerActionDescriptor? cad = route.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (cad is not null)
            return string.Concat(cad.ControllerName, ".", cad.ActionName);

        return route.DisplayName;
    }
}
