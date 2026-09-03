using CacheOrchestrator.Edge.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Edge.Providers;

internal sealed record ResolvedEdgeInstance(
    string Name,
    string TagNamespace,
    IEdgeResponseProvider ResponseProvider,
    IEdgeInvalidationProvider InvalidationProvider);

internal sealed class EdgeInstanceResolver
{
    private readonly IOptionsMonitor<CacheOrchestratorEdgeOptions> _options;
    private readonly IOptionsMonitor<CacheOrchestrator.Configuration.CacheOrchestratorOptions> _coreOptions;
    private readonly EdgeProviderCatalog _providers;

    public EdgeInstanceResolver(
        IOptionsMonitor<CacheOrchestratorEdgeOptions> options,
        IOptionsMonitor<CacheOrchestrator.Configuration.CacheOrchestratorOptions> coreOptions,
        EdgeProviderCatalog providers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(coreOptions);
        ArgumentNullException.ThrowIfNull(providers);
        _options = options;
        _coreOptions = coreOptions;
        _providers = providers;
    }

    public ResolvedEdgeInstance Resolve(string name)
    {
        if (!_options.CurrentValue.EdgeInstances.TryGetValue(name, out EdgeInstanceOptions? instance))
        {
            throw new InvalidOperationException($"edge instance '{name}' is not configured.");
        }

        ResolvedEdgeProvider provider = _providers.Resolve(instance.Provider);
        if (!provider.Invalidation.Capabilities.SupportsTagInvalidation)
        {
            throw new InvalidOperationException(
                $"Edge provider '{provider.Invalidation.Name}' does not support tag invalidation.");
        }

        string tagNamespace = !string.IsNullOrWhiteSpace(instance.Namespace)
            ? instance.Namespace
            : $"{_coreOptions.CurrentValue.Namespace ?? "app-cache"}-edge-{name}";
        return new ResolvedEdgeInstance(name, tagNamespace, provider.Response, provider.Invalidation);
    }
}
