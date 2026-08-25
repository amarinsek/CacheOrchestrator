using StackExchange.Redis;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Builds StackExchange.Redis <see cref="ConfigurationOptions"/> from <see cref="RedisConnectionOptions"/>.
/// </summary>
public static class RedisConfigurationOptionsFactory
{
    /// <summary>
    /// Parses the connection string and applies timeout / keep-alive settings.
    /// </summary>
    public static ConfigurationOptions Create(RedisConnectionOptions redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        if (string.IsNullOrWhiteSpace(redis.Configuration))
            throw new ArgumentException("Redis Configuration (connection string) is required.", nameof(redis));

        ConfigurationOptions options = ConfigurationOptions.Parse(redis.Configuration);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = redis.ConnectTimeout;
        options.SyncTimeout = redis.SyncTimeout;
        options.KeepAlive = redis.KeepAliveSeconds;
        return options;
    }
}
