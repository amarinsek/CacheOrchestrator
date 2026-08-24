using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CacheOrchestrator.FusionCache.Backends;

/// <summary>
/// Mutable registry of <see cref="IFusionCacheBackendRegistrar"/> instances used during DI setup.
/// </summary>
public sealed class FusionCacheBackendRegistrarRegistry
{
    private readonly Dictionary<string, IFusionCacheBackendRegistrar> _registrars =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds or replaces a registrar by <see cref="IFusionCacheBackendRegistrar.Name"/>.</summary>
    public void Add(IFusionCacheBackendRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        if (string.IsNullOrWhiteSpace(registrar.Name))
            throw new ArgumentException("Registrar Name cannot be null or empty.", nameof(registrar));

        _registrars[registrar.Name] = registrar;
    }

    /// <summary>Resolves a registrar by provider name.</summary>
    public IFusionCacheBackendRegistrar Resolve(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        if (_registrars.TryGetValue(providerName, out IFusionCacheBackendRegistrar? registrar))
            return registrar;

        throw new InvalidOperationException(
            $"Unsupported FusionCache provider '{providerName}'. Supported values are: {string.Join(", ", _registrars.Keys)}. " +
            "Register a backend with AddRedisBackend() or AddFusionBackend(...).");
    }

    /// <summary>Gets or creates the registry on <paramref name="services"/> (InMemory pre-registered).</summary>
    public static FusionCacheBackendRegistrarRegistry GetOrCreate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        for (int i = 0; i < services.Count; i++)
        {
            ServiceDescriptor descriptor = services[i];
            if (descriptor.ServiceType == typeof(FusionCacheBackendRegistrarRegistry)
                && descriptor.ImplementationInstance is FusionCacheBackendRegistrarRegistry existing)
            {
                return existing;
            }
        }

        FusionCacheBackendRegistrarRegistry registry = new();
        registry.Add(new InMemoryFusionCacheBackendRegistrar());
        services.TryAddSingleton(registry);
        return registry;
    }
}
