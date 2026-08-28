using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.HybridCache;

internal sealed class HybridCacheOptionsValidator : IValidateOptions<CacheOrchestratorOptions>
{
    public ValidateOptionsResult Validate(string? name, CacheOrchestratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string? unsupported = options.DataCacheInstances.Keys
            .FirstOrDefault(static key => !string.Equals(key, "default", StringComparison.OrdinalIgnoreCase));
        if (unsupported is not null)
        {
            return ValidateOptionsResult.Fail(
                $"HybridCache uses one DI cache and does not support named Data Cache instance '{unsupported}'. " +
                "Use only DataCacheInstances:default or select FusionCache for named instances.");
        }

        return ValidateOptionsResult.Success;
    }
}
