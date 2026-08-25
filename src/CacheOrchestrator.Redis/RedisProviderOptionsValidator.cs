using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Redis;

/// <summary>
/// Meta validator: Output Cache Redis + Fusion data-cache Redis instances.
/// Registered by <see cref="CacheOrchestratorRedisBuilderExtensions.AddRedisBackend"/>.
/// Leaf packages register surface-specific validators when used alone.
/// </summary>
internal sealed class RedisProviderOptionsValidator : IValidateOptions<CacheOrchestratorOptions>
{
    private readonly RedisOutputCacheProviderOptionsValidator _outputCache;
    private readonly RedisFusionCacheProviderOptionsValidator _fusionCache;

    public RedisProviderOptionsValidator(IConfiguration configuration, string configSection)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);
        _outputCache = new RedisOutputCacheProviderOptionsValidator(configuration, configSection);
        _fusionCache = new RedisFusionCacheProviderOptionsValidator(configuration, configSection);
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CacheOrchestratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ValidateOptionsResult oc = _outputCache.Validate(name, options);
        ValidateOptionsResult fc = _fusionCache.Validate(name, options);

        if (oc.Succeeded && fc.Succeeded)
            return ValidateOptionsResult.Success;

        List<string> failures = [];
        if (oc.Failed && oc.Failures is not null)
            failures.AddRange(oc.Failures);
        if (fc.Failed && fc.Failures is not null)
            failures.AddRange(fc.Failures);

        return ValidateOptionsResult.Fail(failures);
    }
}
