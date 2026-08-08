using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Validates that Redis is configured whenever <c>Provider</c> is <c>Redis</c>.
/// Registered by <see cref="CacheOrchestratorRedisBuilderExtensions.AddRedisBackend"/>.
/// </summary>
internal sealed class RedisProviderOptionsValidator : IValidateOptions<CacheOrchestratorOptions>
{
    private readonly IConfiguration _configuration;
    private readonly string _configSection;

    public RedisProviderOptionsValidator(IConfiguration configuration, string configSection)
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

        if (string.Equals(options.OutputCache.Provider, RedisConfiguration.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            RedisConnectionOptions redis = RedisConfiguration.ResolveForOutputCache(_configuration, _configSection);
            if (string.IsNullOrWhiteSpace(redis.Configuration))
            {
                failures.Add(
                    "OutputCache.Provider is 'Redis' but no connection string was found. " +
                    $"Set '{_configSection}:Redis:Configuration' or '{_configSection}:OutputCache:Redis:Configuration'.");
            }
        }

        foreach ((string? instanceName, CacheOrchestratorOptions.FusionCacheInstanceOptions? instanceOpts) in options.FusionCacheInstances)
        {
            if (!string.Equals(instanceOpts.Provider, RedisConfiguration.ProviderName, StringComparison.OrdinalIgnoreCase))
                continue;

            RedisConnectionOptions redis =
                RedisConfiguration.ResolveForFusionInstance(_configuration, _configSection, instanceName);
            if (string.IsNullOrWhiteSpace(redis.Configuration))
            {
                failures.Add(
                    $"FusionCacheInstances['{instanceName}'].Provider is 'Redis' but no connection string was found. " +
                    $"Set '{_configSection}:Redis:Configuration' or " +
                    $"'{_configSection}:FusionCacheInstances:{instanceName}:Redis:Configuration'.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
