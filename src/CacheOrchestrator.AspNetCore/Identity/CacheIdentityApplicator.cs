using CacheOrchestrator.Configuration;
using CacheOrchestrator.Vary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.Identity;

/// <summary>
/// Builds identity material for a binding and applies it to Output Cache / request feature state.
/// </summary>
internal static class CacheIdentityApplicator
{
    internal const string VaryValuePrefix = "co-id:";

    public static async ValueTask<CacheIdentityMaterial?> BuildAsync(
        CacheIdentityBinding binding,
        HttpContext http,
        DomainHttpCacheOptions options,
        CacheVarySurface surface,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        switch (binding.Kind)
        {
            case CacheIdentityKind.Url:
                return CacheIdentityMaterial.Empty;

            case CacheIdentityKind.ContentHash:
                return await CacheIdentityBodyHasher
                    .HashAsync(http.Request, binding.MaxBodyBytes, logger, cancellationToken)
                    .ConfigureAwait(false);

            case CacheIdentityKind.NamedContract:
                if (binding.Contract is null)
                {
                    throw new InvalidOperationException(
                        $"Cache identity contract '{binding.ContractName}' was not resolved onto the endpoint. " +
                        "Ensure AddCacheIdentityContract<T>() is called and the host has started.");
                }

                CacheIdentityContext context = new()
                {
                    HttpContext = http,
                    Options = options,
                    Surface = surface,
                };
                return await binding.Contract.BuildAsync(context, cancellationToken).ConfigureAwait(false);

            default:
                return CacheIdentityMaterial.Empty;
        }
    }

    public static void ApplyToOutputCache(OutputCacheContext context, CacheIdentityMaterial material)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(material);

        foreach ((string key, string value) in material.Values)
            context.CacheVaryByRules.VaryByValues[VaryValuePrefix + key] = value;
    }

    public static void StoreOnFeature(
        HttpContext http,
        CacheIdentityMaterial? material,
        bool bypass,
        ILogger? logger)
    {
        CacheIdentityFeature identity = CacheIdentityFeatureAccessor.GetOrCreate(http);
        identity.Material = material;
        identity.Resolved = true;
        identity.Bypass = bypass;

        if (bypass && logger is not null && logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Cache identity returned null material; caching bypassed for this request.");
    }

}
