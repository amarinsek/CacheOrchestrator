using Microsoft.Extensions.Configuration;

namespace CacheOrchestrator.Backends;

/// <summary>
/// Standard configuration paths for backend-specific settings under the Cache section.
/// </summary>
/// <remarks>
/// Custom backends should bind options from these sections so apps get a consistent
/// <c>appsettings.json</c> shape:
/// <list type="bullet">
/// <item><c>{section}:OutputCache:{Provider}</c> — e.g. <c>Cache:OutputCache:SqlServer</c></item>
/// <item><c>{section}:DataCacheInstances:{instance}:{Provider}</c> — e.g. <c>Cache:DataCacheInstances:default:SqlServer</c></item>
/// </list>
/// </remarks>
internal static class BackendConfiguration
{
    /// <summary>
    /// Section for Output Cache backend settings: <c>{configSection}:OutputCache:{providerName}</c>.
    /// </summary>
    public static IConfigurationSection GetOutputBackendSection(
        IConfiguration configuration,
        string configSection,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        return configuration.GetSection($"{configSection}:OutputCache:{providerName}");
    }

    /// <summary>
    /// Section for a named FusionCache instance backend:
    /// <c>{configSection}:DataCacheInstances:{instanceName}:{providerName}</c>.
    /// </summary>
    public static IConfigurationSection GetFusionBackendSection(
        IConfiguration configuration,
        string configSection,
        string instanceName,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        return configuration.GetSection($"{configSection}:DataCacheInstances:{instanceName}:{providerName}");
    }
}
