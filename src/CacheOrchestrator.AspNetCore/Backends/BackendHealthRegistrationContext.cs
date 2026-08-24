using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.Backends;

/// <summary>
/// Context passed to <see cref="ICacheBackendRegistrar.RegisterHealthProbes"/>.
/// </summary>
public sealed class BackendHealthRegistrationContext
{
    internal BackendHealthRegistrationContext(
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

    /// <summary>
    /// Logical name for this probe target: FusionCache instance name, or <c>oc</c> for Output Cache.
    /// </summary>
    public string InstanceName { get; }

    /// <summary>Provider name for this registration.</summary>
    public string ProviderName { get; }

    /// <summary>Root orchestrator options.</summary>
    public CacheOrchestratorOptions RootOptions { get; }

    /// <summary>
    /// Per-instance options. For Output Cache health, this may be an empty placeholder instance.
    /// </summary>
    public CacheOrchestratorOptions.DataCacheInstanceOptions InstanceOptions { get; }

    /// <summary>
    /// Fusion backend section when <see cref="InstanceName"/> is a Fusion instance;
    /// Output backend section when registering Output Cache probes (<c>oc</c>).
    /// </summary>
    public IConfigurationSection BackendSection =>
        string.Equals(InstanceName, "oc", StringComparison.OrdinalIgnoreCase)
            ? BackendConfiguration.GetOutputBackendSection(Configuration, ConfigSection, ProviderName)
            : BackendConfiguration.GetFusionBackendSection(Configuration, ConfigSection, InstanceName, ProviderName);
}
