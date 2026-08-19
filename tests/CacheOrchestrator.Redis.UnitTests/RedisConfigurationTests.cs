using CacheOrchestrator.Redis;
using Microsoft.Extensions.Configuration;

namespace CacheOrchestrator.Redis.UnitTests;

public class RedisConfigurationTests
{
    [Fact]
    public void ResolveForOutputCache_UsesGlobalWhenLocalMissing()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "global:6379",
                ["Cache:Redis:ConnectTimeout"] = "1111"
            })
            .Build();

        RedisConnectionOptions redis = RedisConfiguration.ResolveForOutputCache(config, "Cache");

        redis.Configuration.Should().Be("global:6379");
        redis.ConnectTimeout.Should().Be(1111);
    }

    [Fact]
    public void ResolveForOutputCache_LocalOverridesGlobal()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "global:6379",
                ["Cache:OutputCache:Redis:Configuration"] = "output:6380",
                ["Cache:OutputCache:Redis:ConnectTimeout"] = "2222"
            })
            .Build();

        RedisConnectionOptions redis = RedisConfiguration.ResolveForOutputCache(config, "Cache");

        redis.Configuration.Should().Be("output:6380");
        redis.ConnectTimeout.Should().Be(2222);
    }

    [Fact]
    public void ResolveForFusionInstance_LocalOverridesGlobal()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "global:6379",
                ["Cache:FusionCacheInstances:pii:Redis:Configuration"] = "pii:6390"
            })
            .Build();

        RedisConnectionOptions redis = RedisConfiguration.ResolveForFusionInstance(config, "Cache", "pii");

        redis.Configuration.Should().Be("pii:6390");
    }
}
