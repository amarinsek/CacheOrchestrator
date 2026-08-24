using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.FusionCache.Backends;

/// <summary>
/// Context passed to <see cref="IFusionCacheBackendRegistrar.RegisterHealthProbes"/>.
/// </summary>
public sealed class FusionBackendHealthRegistrationContext
{
    internal FusionBackendHealthRegistrationContext(
        IServiceCollection services,
        IConfiguration configuration,
        string configSection,
        string instanceName,
        string providerName,
        CacheOrchestratorOptions rootOptions,
        CacheOrchestratorOptions.DataCacheInstanceOptions instanceOptions)
    {
        Services = services;
        Configuration = configuration;
        ConfigSection = configSection;
        InstanceName = instanceName;
        ProviderName = providerName;
        RootOptions = rootOptions;
        InstanceOptions = instanceOptions;
    }

    /// <summary>The application service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Root application configuration.</summary>
    public IConfiguration Configuration { get; }

    /// <summary>Configuration section name (default <c>Cache</c>).</summary>
    public string ConfigSection { get; }

    /// <summary>Logical FusionCache instance name.</summary>
    public string InstanceName { get; }

    /// <summary>Provider name for this registration.</summary>
    public string ProviderName { get; }

    /// <summary>Root orchestrator options.</summary>
    public CacheOrchestratorOptions RootOptions { get; }

    /// <summary>Per-instance options.</summary>
    public CacheOrchestratorOptions.DataCacheInstanceOptions InstanceOptions { get; }

    /// <summary>
    /// Section <c>{ConfigSection}:DataCacheInstances:{InstanceName}:{ProviderName}</c>.
    /// </summary>
    public IConfigurationSection BackendSection =>
        Configuration.GetSection($"{ConfigSection}:DataCacheInstances:{InstanceName}:{ProviderName}");
}
