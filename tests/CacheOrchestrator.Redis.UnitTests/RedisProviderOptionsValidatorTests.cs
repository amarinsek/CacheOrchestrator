using CacheOrchestrator.Configuration;
using CacheOrchestrator.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis.UnitTests;

public class RedisProviderOptionsValidatorTests
{
    [Fact]
    public void Validate_RedisOutputWithoutConnection_Fails()
    {
        var config = new ConfigurationBuilder().Build();
        var validator = new RedisProviderOptionsValidator(config, "Cache");
        var options = new CacheOrchestratorOptions
        {
            OutputCache = { Provider = "Redis" }
        };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("OutputCache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RedisFusionWithoutConnection_Fails()
    {
        var config = new ConfigurationBuilder().Build();
        var validator = new RedisProviderOptionsValidator(config, "Cache");
        var options = new CacheOrchestratorOptions
        {
            FusionCacheInstances =
            {
                ["default"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Provider = "Redis" }
            }
        };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("FusionCacheInstances['default']", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RedisWithGlobalConnection_Succeeds()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Redis:Configuration"] = "localhost:6379"
            })
            .Build();
        var validator = new RedisProviderOptionsValidator(config, "Cache");
        var options = new CacheOrchestratorOptions
        {
            OutputCache = { Provider = "Redis" },
            FusionCacheInstances =
            {
                ["default"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Provider = "Redis" }
            }
        };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_InMemoryProviders_SucceedsWithoutRedisSection()
    {
        var validator = new RedisProviderOptionsValidator(new ConfigurationBuilder().Build(), "Cache");
        var options = new CacheOrchestratorOptions
        {
            OutputCache = { Provider = "InMemory" },
            FusionCacheInstances =
            {
                ["default"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Provider = "InMemory" }
            }
        };

        validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_CustomSection_UsesThatPrefixInError()
    {
        var validator = new RedisProviderOptionsValidator(new ConfigurationBuilder().Build(), "MyCache");
        var options = new CacheOrchestratorOptions { OutputCache = { Provider = "Redis" } };

        ValidateOptionsResult result = validator.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("MyCache:Redis:Configuration", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenOptionsAreNull_Throws()
    {
        var validator = new RedisProviderOptionsValidator(new ConfigurationBuilder().Build(), "Cache");
        var act = () => validator.Validate(null, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WhenConfigSectionIsWhitespace_Throws()
    {
        var act = () => new RedisProviderOptionsValidator(new ConfigurationBuilder().Build(), " ");
        act.Should().Throw<ArgumentException>();
    }
}
