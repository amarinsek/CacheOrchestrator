using CacheOrchestrator.Configuration;
using CacheOrchestrator.Redis;
using Microsoft.Extensions.Configuration;

namespace CacheOrchestrator.UnitTests.Redis;

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
}
