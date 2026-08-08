namespace CacheOrchestrator.Redis;

/// <summary>
/// Redis connection settings for CacheOrchestrator.Redis.
/// Bound from <c>Cache:Redis</c> and optional overrides under
/// <c>Cache:OutputCache:Redis</c> / <c>Cache:FusionCacheInstances:&#123;name&#125;:Redis</c>.
/// </summary>
public sealed class RedisConnectionOptions
{
    /// <summary>StackExchange.Redis configuration / connection string.</summary>
    public string? Configuration { get; set; }

    /// <summary>Connect timeout in milliseconds. Default: 5000.</summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>Sync timeout in milliseconds. Default: 5000.</summary>
    public int SyncTimeout { get; set; } = 5000;

    /// <summary>TCP keep-alive interval in seconds. Default: 60.</summary>
    public int KeepAliveSeconds { get; set; } = 60;
}
