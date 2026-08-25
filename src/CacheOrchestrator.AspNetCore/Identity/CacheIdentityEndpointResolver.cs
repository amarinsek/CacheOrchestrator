using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.Identity;

/// <summary>
/// Resolves named <see cref="ICacheIdentityContract"/> instances onto endpoint metadata.
/// Invoked at host start so unknown contract names fail before traffic.
/// </summary>
internal static class CacheIdentityEndpointResolver
{
    public static void ResolveAll(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        CacheIdentityContractCatalog catalog =
            services.GetRequiredService<CacheIdentityContractCatalog>();
        EndpointDataSource dataSource =
            services.GetRequiredService<EndpointDataSource>();

        foreach (Endpoint endpoint in dataSource.Endpoints)
            ResolveEndpoint(endpoint, catalog);
    }

    /// <summary>
    /// Ensures a single endpoint's identity bindings are resolved (lazy path when startup deferred).
    /// </summary>
    public static void EnsureResolved(Endpoint endpoint, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(services);

        CacheIdentityEndpointMetadata? metadata =
            endpoint.Metadata.GetMetadata<CacheIdentityEndpointMetadata>();
        if (metadata is null || metadata.IsResolved)
            return;

        bool needsCatalog = false;
        foreach (CacheIdentityBinding binding in metadata.Bindings.Values)
        {
            if (binding.Kind == CacheIdentityKind.NamedContract)
            {
                needsCatalog = true;
                break;
            }
        }

        if (!needsCatalog)
        {
            metadata.MarkResolved();
            return;
        }

        CacheIdentityContractCatalog catalog =
            services.GetRequiredService<CacheIdentityContractCatalog>();
        ResolveEndpoint(endpoint, catalog);
    }

    public static void ResolveEndpoint(Endpoint endpoint, CacheIdentityContractCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(catalog);

        CacheIdentityEndpointMetadata? metadata =
            endpoint.Metadata.GetMetadata<CacheIdentityEndpointMetadata>();
        if (metadata is null || metadata.IsResolved)
            return;

        string displayName = endpoint.DisplayName ?? "(unnamed endpoint)";
        foreach ((string method, CacheIdentityBinding binding) in metadata.Bindings)
        {
            if (binding.Kind != CacheIdentityKind.NamedContract)
                continue;

            string? name = binding.ContractName;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(
                    $"Cache identity binding for '{method}' on '{displayName}' is missing a contract name.");
            }

            if (!catalog.TryGet(name, out ICacheIdentityContract contract))
            {
                throw new InvalidOperationException(
                    $"Unknown cache identity contract '{name}' on endpoint '{displayName}' (method '{method}'). " +
                    "Register it with AddCacheIdentityContract<T>().");
            }

            binding.SetContract(contract);
        }

        metadata.MarkResolved();
    }
}
