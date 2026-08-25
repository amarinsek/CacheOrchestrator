using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Validates Redis connection settings when a data-cache instance uses <c>Provider: Redis</c>.
/// </summary>
internal sealed class RedisFusionCacheProviderOptionsValidator : IValidateOptions<CacheOrchestratorOptions>
{
    private readonly IConfiguration _configuration;
    private readonly string _configSection;

    public RedisFusionCacheProviderOptionsValidator(IConfiguration configuration, string configSection)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);
        _configuration = configuration;
        _configSection = configSection;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CacheOrchestratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        foreach ((string? instanceName, CacheOrchestratorOptions.DataCacheInstanceOptions? instanceOpts) in options.DataCacheInstances)
        {
            if (!string.Equals(instanceOpts.Provider, RedisConfiguration.ProviderName, StringComparison.OrdinalIgnoreCase))
                continue;

            RedisConnectionOptions redis =
                RedisConfiguration.ResolveForFusionInstance(_configuration, _configSection, instanceName);
            if (string.IsNullOrWhiteSpace(redis.Configuration))
            {
                failures.Add(
                    $"DataCacheInstances['{instanceName}'].Provider is 'Redis' but no connection string was found. " +
                    $"Set '{_configSection}:Redis:Configuration' or " +
                    $"'{_configSection}:DataCacheInstances:{instanceName}:Redis:Configuration'.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
