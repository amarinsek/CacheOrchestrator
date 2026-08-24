using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// DI registration for the FusionCache <see cref="IDataCacheProvider"/>.
/// </summary>
public static class CacheOrchestratorFusionCacheServiceExtensions
{
    /// <summary>
    /// Registers <see cref="FusionDataCacheProvider"/> as the default <see cref="IDataCacheProvider"/>
    /// and Fusion domain settings resolution (<c>{configSection}:…:FusionCache</c> sections).
    /// Named FusionCache instances must still be registered by the host (e.g. via AspNetCore <c>AddCacheOrchestrator</c>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configSection">Root configuration section (default <c>Cache</c>).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCacheOrchestratorFusionCache(
        this IServiceCollection services,
        string configSection = "Cache")
    {
        ArgumentNullException.ThrowIfNull(services);

        DomainSettingCatalog.RegisterSection(
            typeof(DomainFusionCacheSettings),
            idPrefix: "fusionCache",
            propertyPrefix: "FusionCache");

        string section = string.IsNullOrWhiteSpace(configSection) ? "Cache" : configSection;

        services.TryAddSingleton<IFusionDomainRuntimeOverrideStore, FusionDomainRuntimeOverrideStore>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDomainSettingsPatchContributor, FusionDomainSettingsPatchContributor>());
        services.TryAddSingleton<IFusionDomainSettingsProvider>(sp =>
            new FusionDomainSettingsProvider(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<IOptionsMonitor<CacheOrchestratorOptions>>(),
                sp.GetService<IFusionDomainRuntimeOverrideStore>(),
                section));
        services.TryAddSingleton<IDataCacheProvider, FusionDataCacheProvider>();
        return services;
    }
}
