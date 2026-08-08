using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Validates core <see cref="CacheOrchestratorOptions"/> at startup (provider names, TTLs,
/// FusionCache instance references, Output Cache capability).
/// Provider-specific connection rules (e.g. Redis) are validated by the corresponding package.
/// </summary>
internal sealed class CacheOrchestratorOptionsValidator : IValidateOptions<CacheOrchestratorOptions>
{
    private readonly HashSet<string> _validProviders;
    private readonly IReadOnlyDictionary<string, bool> _supportsOutputCache;

    public CacheOrchestratorOptionsValidator(
        IEnumerable<string> validProviders,
        IReadOnlyDictionary<string, bool>? supportsOutputCache = null)
    {
        ArgumentNullException.ThrowIfNull(validProviders);
        _validProviders = new HashSet<string>(validProviders, StringComparer.OrdinalIgnoreCase);
        _supportsOutputCache = supportsOutputCache
            ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CacheOrchestratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (!_validProviders.Contains(options.OutputCache.Provider))
        {
            failures.Add(
                $"OutputCache.Provider must be one of: {string.Join(", ", _validProviders)}. " +
                $"Current value: '{options.OutputCache.Provider}'.");
        }
        else if (_supportsOutputCache.TryGetValue(options.OutputCache.Provider, out bool supportsOc)
                 && !supportsOc)
        {
            failures.Add(
                $"OutputCache.Provider '{options.OutputCache.Provider}' does not support an Output Cache store " +
                $"(SupportsOutputCacheStore = false). Use a provider that implements an Output Cache store " +
                $"(e.g. InMemory, or Redis via CacheOrchestrator.Redis).");
        }

        if (options.FusionCacheInstances.Count == 0)
        {
            failures.Add("FusionCacheInstances must contain at least one entry named 'default'.");
        }
        else
        {
            if (!options.FusionCacheInstances.ContainsKey("default"))
                failures.Add("FusionCacheInstances must contain an entry named 'default'.");

            foreach ((string? instanceName, CacheOrchestratorOptions.FusionCacheInstanceOptions? instanceOpts) in options.FusionCacheInstances)
            {
                if (!_validProviders.Contains(instanceOpts.Provider))
                {
                    failures.Add(
                        $"FusionCacheInstances['{instanceName}'].Provider must be one of: {string.Join(", ", _validProviders)}. " +
                        $"Current value: '{instanceOpts.Provider}'.");
                }
            }
        }

        ValidateDomainSettings("DomainDefaults", options.DomainDefaults, failures);

        foreach ((string? domain, CacheOrchestratorOptions.DomainCacheSettings? settings) in options.Domains)
        {
            ValidateDomainSettings($"Domain '{domain}'", settings, failures);

            if (!string.IsNullOrWhiteSpace(settings.FusionCacheInstance) &&
                !options.FusionCacheInstances.ContainsKey(settings.FusionCacheInstance))
            {
                failures.Add(
                    $"Domain '{domain}': FusionCacheInstance '{settings.FusionCacheInstance}' " +
                    $"does not exist in FusionCacheInstances.");
            }
        }

        DistributedResilienceOptions resilience = options.GetEffectiveDistributedResilience();
        if (resilience.SoftTimeoutSeconds < 0)
            failures.Add("Distributed.SoftTimeoutSeconds cannot be negative.");
        if (resilience.HardTimeoutSeconds < 0)
            failures.Add("Distributed.HardTimeoutSeconds cannot be negative.");
        if (resilience.CircuitBreakerSeconds < 0)
            failures.Add("Distributed.CircuitBreakerSeconds cannot be negative.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateDomainSettings(
        string label,
        CacheOrchestratorOptions.DomainCacheSettings settings,
        List<string> failures)
    {
        if (settings.OutputCacheTtlSeconds is < 0)
            failures.Add($"{label}: OutputCacheTtlSeconds cannot be negative.");

        if (settings.FusionCacheSoftTtlSeconds is < 0)
            failures.Add($"{label}: FusionCacheSoftTtlSeconds cannot be negative.");

        if (settings.FusionCacheHardTtlSeconds is < 0)
            failures.Add($"{label}: FusionCacheHardTtlSeconds cannot be negative.");
    }
}
