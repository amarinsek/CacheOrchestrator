using Microsoft.Extensions.Logging;
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
    private readonly ILogger? _logger;

    public CacheOrchestratorOptionsValidator(
        IEnumerable<string> validProviders,
        IReadOnlyDictionary<string, bool>? supportsOutputCache = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(validProviders);
        _validProviders = new HashSet<string>(validProviders, StringComparer.OrdinalIgnoreCase);
        _supportsOutputCache = supportsOutputCache
            ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
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
            ValidateDomainKey(domain, failures);

            ValidateDomainSettings($"Domain '{domain}'", settings, failures);

            string? dataInstance = settings.DataCache?.Instance;
            if (!string.IsNullOrWhiteSpace(dataInstance) &&
                !options.FusionCacheInstances.ContainsKey(dataInstance))
            {
                failures.Add(
                    $"Domain '{domain}': DataCache.Instance '{dataInstance}' " +
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

    private void ValidateDomainKey(string? domain, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            failures.Add("Domain name cannot be null or whitespace.");
            return;
        }

        string normalized = DomainName.Normalize(domain);

        // Unusable keys collapse to "default" and would collide with the real default domain.
        if (normalized == DomainName.Default
            && !string.Equals(domain.Trim(), DomainName.Default, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"Domain name '{domain}' normalizes to '{DomainName.Default}' and cannot be used as a Domains key.");
            return;
        }

        // Domains lookup is OrdinalIgnoreCase on the normalized name. Keys that change beyond
        // case (spaces, invalid chars, collapsed dashes) would never match at runtime.
        if (!string.Equals(domain, normalized, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"Domain name '{domain}' is invalid. After normalization it becomes '{normalized}', " +
                $"which would not match this Domains key under case-insensitive lookup. " +
                $"Use '{normalized}' as the Domains key.");
            return;
        }

        // Case-only differences (e.g. MyStore) still resolve, but miss the zero-alloc Normalize fast path.
        if (!DomainName.IsNormalized(domain) && _logger is not null && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "Domains key '{Domain}' is not fully normalized (preferred form: '{Normalized}'). " +
                "The domain still works, but DomainName.Normalize allocates on the hot path. " +
                "Use lowercase letters, digits, and '-', ':', '_', '@' in appsettings and [CacheDomain] names.",
                domain,
                normalized);
        }
    }

    private static void ValidateDomainSettings(
        string label,
        CacheOrchestratorOptions.DomainCacheSettings settings,
        List<string> failures)
    {
        ValidateNonNegTimeSpan(label, "DataCache.Ttl", settings.DataCache?.Ttl, failures);
        ValidateNonNegTimeSpan(label, "OutputCache.Ttl", settings.OutputCache?.Ttl, failures);
        ValidateNonNegTimeSpan(label, "ClientCache.Ttl", settings.ClientCache?.Ttl, failures);
        ValidateNonNegTimeSpan(label, "ClientCache.TtlMin", settings.ClientCache?.TtlMin, failures);
        ValidateNonNegTimeSpan(label, "FusionCache.HardTtl", settings.FusionCache?.HardTtl, failures);
        ValidateNonNegTimeSpan(label, "FusionCache.FailSafe", settings.FusionCache?.FailSafe, failures);
        ValidateNonNegTimeSpan(label, "FusionCache.Jitter", settings.FusionCache?.Jitter, failures);
        ValidateNonNegTimeSpan(label, "FusionCache.FactorySoftTimeout", settings.FusionCache?.FactorySoftTimeout, failures);
        ValidateNonNegTimeSpan(label, "FusionCache.FactoryHardTimeout", settings.FusionCache?.FactoryHardTimeout, failures);

        if (settings.FusionCache?.EagerRefreshRatio is double ratio && (ratio < 0 || ratio >= 1))
            failures.Add($"{label}: FusionCache.EagerRefreshRatio must be 0 (disabled) or in (0, 1).");

        ValidateAllowlist(label, "VaryByHeaders", settings.VaryByHeaders, Vary.CacheVaryMaterializer.MaxVaryByHeaders, failures, allowEmpty: true);
        ValidateAllowlist(label, "VaryByCookies", settings.VaryByCookies, Vary.CacheVaryMaterializer.MaxVaryByCookies, failures, allowEmpty: true);
        ValidateAllowlist(label, "VaryByQueryKeys", settings.VaryByQueryKeys, max: 32, failures, allowEmpty: true);
        ValidateAllowlist(label, "IgnoreQueryKeys", settings.IgnoreQueryKeys, max: 32, failures, allowEmpty: true);
        ValidateAllowlist(label, "VaryByAuthClaims", settings.VaryByAuthClaims, max: 16, failures, allowEmpty: true);
        ValidateAllowlist(label, "AcceptNormalizationList", settings.AcceptNormalizationList, max: 16, failures, allowEmpty: true);
        ValidateAllowlist(label, "AcceptLanguageNormalizationList", settings.AcceptLanguageNormalizationList, max: 16, failures, allowEmpty: true);

        if (settings.AuthBypassMode is AuthBypassMode mode && !Enum.IsDefined(mode))
            failures.Add($"{label}: AuthBypassMode value '{mode}' is not defined.");
    }

    private static void ValidateNonNegTimeSpan(
        string label,
        string propertyName,
        TimeSpan? value,
        List<string> failures)
    {
        if (value is { } t && t < TimeSpan.Zero)
            failures.Add($"{label}: {propertyName} cannot be negative.");
    }

    internal static void ValidateAllowlist(
        string label,
        string propertyName,
        string[]? values,
        int max,
        List<string> failures,
        bool allowEmpty = false)
    {
        if (values is null)
            return;

        if (values.Length == 0)
        {
            if (!allowEmpty)
                failures.Add($"{label}: {propertyName} must not be empty.");
            return;
        }

        if (values.Length > max)
        {
            failures.Add($"{label}: {propertyName} cannot contain more than {max} entries (got {values.Length}).");
            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i]))
                failures.Add($"{label}: {propertyName}[{i}] must not be null or whitespace.");
        }
    }
}
