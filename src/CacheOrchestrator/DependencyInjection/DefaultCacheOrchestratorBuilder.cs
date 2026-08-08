using CacheOrchestrator.Backends;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.DependencyInjection;

internal sealed class DefaultCacheOrchestratorBuilder : ICacheOrchestratorBuilder
{
    private readonly Dictionary<string, ICacheBackendRegistrar> _registrars = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action<OutputCacheOptions>> _outputCacheConfigurators = [];

    public DefaultCacheOrchestratorBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
    }

    public IServiceCollection Services { get; }

    public IConfiguration Configuration { get; }

    public ICacheOrchestratorBuilder AddBackend(ICacheBackendRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);

        if (string.IsNullOrWhiteSpace(registrar.Name))
            throw new ArgumentException("Registrar Name cannot be null or empty.", nameof(registrar));

        _registrars[registrar.Name] = registrar;
        return this;
    }

    public ICacheOrchestratorBuilder ConfigureOutputCache(Action<OutputCacheOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _outputCacheConfigurators.Add(configure);
        return this;
    }

    public ICacheBackendRegistrar ResolveRegistrar(string providerName)
    {
        if (_registrars.TryGetValue(providerName, out ICacheBackendRegistrar? registrar))
            return registrar;

        throw new InvalidOperationException(
            $"Unsupported cache provider '{providerName}'. Supported values are: {string.Join(", ", _registrars.Keys)}.");
    }

    public IEnumerable<string> GetRegisteredProviderNames() => _registrars.Keys;

    public IReadOnlyList<Action<OutputCacheOptions>> OutputCacheConfigurators => _outputCacheConfigurators;
}
