using CacheOrchestrator.Backends;
using CacheOrchestrator.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Registers Redis-backed Output Cache, distributed cache, backplane, and health probes.
/// Each FusionCache instance gets its own keyed <see cref="IConnectionMultiplexer"/> and
/// keyed <see cref="IDistributedCache"/> so multiple instances can target different Redis clusters.
/// </summary>
public sealed class RedisCacheBackendRegistrar : ICacheBackendRegistrar
{
    /// <inheritdoc />
    public string Name => RedisConfiguration.ProviderName;

    /// <inheritdoc />
    public bool SupportsOutputCacheStore => true;

    /// <inheritdoc />
    public void RegisterOutputCache(OutputCacheRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        RedisConnectionOptions redis = RedisConfiguration.ResolveForOutputCache(
            context.Configuration, context.ConfigSection);

        if (string.IsNullOrWhiteSpace(redis.Configuration))
        {
            throw new InvalidOperationException(
                "Redis configuration is required when OutputCache.Provider is 'Redis'. " +
                $"Set '{context.ConfigSection}:Redis:Configuration' or " +
                $"'{context.ConfigSection}:OutputCache:Redis:Configuration'.");
        }

        ConfigurationOptions configOptions = CreateConfigurationOptions(redis);

        context.RegisterStore(() =>
        {
            context.Services.TryAddKeyedSingleton<IConnectionMultiplexer>(
                "oc",
                (_, _) => ConnectionMultiplexer.Connect(configOptions));

            context.Services.AddStackExchangeRedisOutputCache(o =>
            {
                o.ConfigurationOptions = configOptions;
                o.InstanceName = context.Options.OutputNamespace;
            });
        });
    }

    /// <inheritdoc />
    public void RegisterFusionCache(FusionCacheRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        RedisConnectionOptions redis = RedisConfiguration.ResolveForFusionInstance(
            context.Configuration, context.ConfigSection, context.InstanceName);

        if (string.IsNullOrWhiteSpace(redis.Configuration))
        {
            throw new InvalidOperationException(
                $"DataCacheInstances['{context.InstanceName}']: Redis configuration is required when Provider is 'Redis'. " +
                $"Set '{context.ConfigSection}:Redis:Configuration' or " +
                $"'{context.ConfigSection}:DataCacheInstances:{context.InstanceName}:Redis:Configuration'.");
        }

        ConfigurationOptions configOptions = CreateConfigurationOptions(redis);
        string fcNamespace = context.InstanceOptions.GetNamespace(context.InstanceName, context.RootOptions);
        string instanceName = context.InstanceName;

        context.Services.TryAddKeyedSingleton<IConnectionMultiplexer>(
            instanceName,
            (_, _) => ConnectionMultiplexer.Connect(configOptions));

        context.Services.TryAddKeyedSingleton<IDistributedCache>(instanceName, (sp, _) =>
        {
            IConnectionMultiplexer mux = sp.GetRequiredKeyedService<IConnectionMultiplexer>(instanceName);
            RedisCacheOptions redisCacheOptions = new()
            {
                ConnectionMultiplexerFactory = () => Task.FromResult(mux),
                InstanceName = fcNamespace
            };

            return new RedisCache(Options.Create(redisCacheOptions));
        });

        context.FusionBuilder.WithRegisteredKeyedDistributedCache(instanceName);

        context.FusionBuilder.WithBackplane(sp =>
        {
            IConnectionMultiplexer mux = sp.GetRequiredKeyedService<IConnectionMultiplexer>(instanceName);
            return new RedisBackplane(new RedisBackplaneOptions
            {
                ConnectionMultiplexerFactory = () => Task.FromResult(mux)
            });
        });

        string backplaneChannel = fcNamespace + ":backplane";
        context.FusionBuilder.WithOptions(o => o.BackplaneChannelPrefix = backplaneChannel);
    }

    /// <inheritdoc />
    public void RegisterHealthProbes(BackendHealthRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string instanceName = context.InstanceName;
        context.Services.AddSingleton<ICacheOrchestratorHealthProbe>(sp =>
        {
            IConnectionMultiplexer mux = sp.GetRequiredKeyedService<IConnectionMultiplexer>(instanceName);
            return new RedisCacheHealthProbe($"redis:{instanceName}", mux);
        });
    }

    private static ConfigurationOptions CreateConfigurationOptions(RedisConnectionOptions redis)
    {
        ConfigurationOptions options = ConfigurationOptions.Parse(redis.Configuration!);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = redis.ConnectTimeout;
        options.SyncTimeout = redis.SyncTimeout;
        options.KeepAlive = redis.KeepAliveSeconds;
        return options;
    }
}
