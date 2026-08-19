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

    [Fact]
    public void ResolveForFusionInstance_UsesGlobalWhenLocalMissing()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "global:6379",
                ["Cache:Redis:SyncTimeout"] = "3333"
            })
            .Build();

        RedisConnectionOptions redis = RedisConfiguration.ResolveForFusionInstance(config, "Cache", "default");

        redis.Configuration.Should().Be("global:6379");
        redis.SyncTimeout.Should().Be(3333);
        redis.ConnectTimeout.Should().Be(5000);
        redis.KeepAliveSeconds.Should().Be(60);
    }

    [Fact]
    public void ResolveForOutputCache_LocalTimeoutsDoNotOverrideWhenUnset()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "global:6379",
                ["Cache:Redis:ConnectTimeout"] = "1111",
                ["Cache:OutputCache:Redis:Configuration"] = "output:6380"
            })
            .Build();

        RedisConnectionOptions redis = RedisConfiguration.ResolveForOutputCache(config, "Cache");

        redis.Configuration.Should().Be("output:6380");
        redis.ConnectTimeout.Should().Be(1111);
    }

    [Fact]
    public void GetGlobalSection_UsesConfigSectionName()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MyCache:Redis:Configuration"] = "custom:6379"
            })
            .Build();

        IConfigurationSection section = RedisConfiguration.GetGlobalSection(config, "MyCache");
        section["Configuration"].Should().Be("custom:6379");
    }

    [Fact]
    public void Bind_EmptySection_UsesDefaults()
    {
        IConfigurationRoot config = new ConfigurationBuilder().Build();
        RedisConnectionOptions options = RedisConfiguration.Bind(config.GetSection("missing"));

        options.Configuration.Should().BeNull();
        options.ConnectTimeout.Should().Be(5000);
        options.SyncTimeout.Should().Be(5000);
        options.KeepAliveSeconds.Should().Be(60);
    }

    [Fact]
    public void ProviderName_IsRedis() => RedisConfiguration.ProviderName.Should().Be("Redis");

    [Fact]
    public void GetGlobalSection_WhenSectionIsWhitespace_Throws()
    {
        IConfigurationRoot config = new ConfigurationBuilder().Build();
        var act = () => RedisConfiguration.GetGlobalSection(config, "  ");
        act.Should().Throw<ArgumentException>();
    }
}
