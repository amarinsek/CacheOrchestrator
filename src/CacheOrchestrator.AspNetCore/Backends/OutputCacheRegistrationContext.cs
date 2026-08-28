using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.Backends;

/// <summary>
/// Context passed to <see cref="IOutputCacheBackendRegistrar.RegisterOutputCache"/> so backends can
/// configure shared <see cref="OutputCacheOptions"/> and/or register a store implementation.
/// </summary>
public sealed class OutputCacheRegistrationContext
{
    private readonly List<Action<OutputCacheOptions>> _optionConfigurators;
    private readonly List<Action> _storeRegistrations = [];

    internal OutputCacheRegistrationContext(
        IServiceCollection services,
        IConfiguration configuration,
        string outputCacheNamespace,
        string configSection,
        string providerName,
        List<Action<OutputCacheOptions>> optionConfigurators)
    {
        Services = services;
        Configuration = configuration;
        OutputCacheNamespace = outputCacheNamespace;
        ConfigSection = configSection;
        ProviderName = providerName;
        _optionConfigurators = optionConfigurators;
    }

    /// <summary>The application service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Root application configuration.</summary>
    public IConfiguration Configuration { get; }

    /// <summary>Effective namespace used to isolate Output Cache keys.</summary>
    public string OutputCacheNamespace { get; }

    /// <summary>Configuration section name (default <c>Cache</c>).</summary>
    public string ConfigSection { get; }

    /// <summary>Provider name for this registration (same as <see cref="IOutputCacheBackendRegistrar.Name"/>).</summary>
    public string ProviderName { get; }

    /// <summary>
    /// Section <c>{ConfigSection}:OutputCache:{ProviderName}</c> for backend-specific settings.
    /// </summary>
    public IConfigurationSection BackendSection =>
        BackendConfiguration.GetOutputBackendSection(Configuration, ConfigSection, ProviderName);

    /// <summary>
    /// Adds a callback that runs when <c>AddOutputCache</c> builds <see cref="OutputCacheOptions"/>
    /// (e.g. in-memory size limits). Call this instead of invoking <c>AddOutputCache</c> yourself.
    /// </summary>
    public void Configure(Action<OutputCacheOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _optionConfigurators.Add(configure);
    }

    /// <summary>
    /// Registers work that must run <strong>after</strong> the shared <c>AddOutputCache</c> call
    /// (e.g. <c>AddStackExchangeRedisOutputCache</c>). Prefer this over calling store APIs that
    /// assume Output Cache services already exist.
    /// </summary>
    public void RegisterStore(Action register)
    {
        ArgumentNullException.ThrowIfNull(register);
        _storeRegistrations.Add(register);
    }

    internal void RunStoreRegistrations()
    {
        foreach (Action register in _storeRegistrations)
            register();
    }
}
