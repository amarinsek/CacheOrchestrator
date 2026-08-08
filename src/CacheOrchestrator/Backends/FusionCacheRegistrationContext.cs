using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.Backends;

/// <summary>
/// Context passed to <see cref="ICacheBackendRegistrar.RegisterFusionCache"/> for one named instance.
/// </summary>
public sealed class FusionCacheRegistrationContext
{
    internal FusionCacheRegistrationContext(
        IServiceCollection services,
        IConfiguration configuration,
        CacheOrchestratorOptions rootOptions,
        string configSection,
        string instanceName,
        CacheOrchestratorOptions.FusionCacheInstanceOptions instanceOptions,
        string providerName,
        IFusionCacheBuilder fusionBuilder,
        DistributedResilienceOptions distributedResilience)
    {
        Services = services;
        Configuration = configuration;
        RootOptions = rootOptions;
        ConfigSection = configSection;
        InstanceName = instanceName;
        InstanceOptions = instanceOptions;
        ProviderName = providerName;
        FusionBuilder = fusionBuilder;
        DistributedResilience = distributedResilience;
    }

    /// <summary>The application service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Root application configuration.</summary>
    public IConfiguration Configuration { get; }

    /// <summary>Root orchestrator options.</summary>
    public CacheOrchestratorOptions RootOptions { get; }

    /// <summary>Configuration section name (default <c>Cache</c>).</summary>
    public string ConfigSection { get; }

    /// <summary>Logical FusionCache instance name (e.g. <c>default</c>, <c>pii</c>).</summary>
    public string InstanceName { get; }

    /// <summary>Per-instance provider and connection settings.</summary>
    public CacheOrchestratorOptions.FusionCacheInstanceOptions InstanceOptions { get; }

    /// <summary>Provider name for this registration.</summary>
    public string ProviderName { get; }

    /// <summary>FusionCache builder for this named instance (serializer and memory cache already configured).</summary>
    public IFusionCacheBuilder FusionBuilder { get; }

    /// <summary>
    /// Effective distributed soft/hard/circuit settings (for L2). Already applied to the builder
    /// when the provider is not InMemory; exposed for custom backends that need the values.
    /// </summary>
    public DistributedResilienceOptions DistributedResilience { get; }

    /// <summary>
    /// Section <c>{ConfigSection}:FusionCacheInstances:{InstanceName}:{ProviderName}</c>.
    /// </summary>
    public IConfigurationSection BackendSection =>
        BackendConfiguration.GetFusionBackendSection(Configuration, ConfigSection, InstanceName, ProviderName);
}
