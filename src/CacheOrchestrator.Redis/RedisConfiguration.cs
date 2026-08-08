using CacheOrchestrator.Backends;
using Microsoft.Extensions.Configuration;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Resolves effective Redis connection settings from the standard configuration paths.
/// </summary>
/// <remarks>
/// Resolution order for connection string and timeouts:
/// <list type="number">
/// <item>Scoped section (Output Cache or Fusion instance <c>Redis</c> child), if present</item>
/// <item>Global <c>{configSection}:Redis</c></item>
/// </list>
/// </remarks>
public static class RedisConfiguration
{
    public const string ProviderName = "Redis";

    /// <summary>
    /// Global Redis section: <c>{configSection}:Redis</c>.
    /// </summary>
    public static IConfigurationSection GetGlobalSection(IConfiguration configuration, string configSection)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);
        return configuration.GetSection($"{configSection}:Redis");
    }

    /// <summary>
    /// Effective options for Output Cache Redis store.
    /// </summary>
    public static RedisConnectionOptions ResolveForOutputCache(
        IConfiguration configuration,
        string configSection)
    {
        IConfigurationSection global = GetGlobalSection(configuration, configSection);
        IConfigurationSection local = BackendConfiguration.GetOutputBackendSection(
            configuration, configSection, ProviderName);
        return Merge(global, local);
    }

    /// <summary>
    /// Effective options for a named FusionCache Redis instance.
    /// </summary>
    public static RedisConnectionOptions ResolveForFusionInstance(
        IConfiguration configuration,
        string configSection,
        string instanceName)
    {
        IConfigurationSection global = GetGlobalSection(configuration, configSection);
        IConfigurationSection local = BackendConfiguration.GetFusionBackendSection(
            configuration, configSection, instanceName, ProviderName);
        return Merge(global, local);
    }

    /// <summary>
    /// Binds a section into <see cref="RedisConnectionOptions"/> (empty section → defaults).
    /// </summary>
    public static RedisConnectionOptions Bind(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        RedisConnectionOptions options = new();
        section.Bind(options);
        return options;
    }

    private static RedisConnectionOptions Merge(IConfigurationSection global, IConfigurationSection local)
    {
        RedisConnectionOptions g = Bind(global);
        RedisConnectionOptions l = Bind(local);

        return new RedisConnectionOptions
        {
            Configuration = !string.IsNullOrWhiteSpace(l.Configuration) ? l.Configuration : g.Configuration,
            // Local section wins when it explicitly sets a value (Bind leaves defaults if missing).
            // Prefer local if its Configuration is set OR if local section exists with any key.
            ConnectTimeout = local.Exists() && local["ConnectTimeout"] is not null ? l.ConnectTimeout : g.ConnectTimeout,
            SyncTimeout = local.Exists() && local["SyncTimeout"] is not null ? l.SyncTimeout : g.SyncTimeout,
            KeepAliveSeconds = local.Exists() && local["KeepAliveSeconds"] is not null ? l.KeepAliveSeconds : g.KeepAliveSeconds
        };
    }
}
