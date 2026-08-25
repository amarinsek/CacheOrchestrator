using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Validates Redis connection settings when <c>OutputCache.Provider</c> is <c>Redis</c>.
/// </summary>
internal sealed class RedisOutputCacheProviderOptionsValidator : IValidateOptions<CacheOrchestratorOptions>
{
    private readonly IConfiguration _configuration;
    private readonly string _configSection;

    public RedisOutputCacheProviderOptionsValidator(IConfiguration configuration, string configSection)
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

        if (!string.Equals(options.OutputCache.Provider, RedisConfiguration.ProviderName, StringComparison.OrdinalIgnoreCase))
            return ValidateOptionsResult.Success;

        RedisConnectionOptions redis = RedisConfiguration.ResolveForOutputCache(_configuration, _configSection);
        if (!string.IsNullOrWhiteSpace(redis.Configuration))
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            "OutputCache.Provider is 'Redis' but no connection string was found. " +
            $"Set '{_configSection}:Redis:Configuration' or '{_configSection}:OutputCache:Redis:Configuration'.");
    }
}
