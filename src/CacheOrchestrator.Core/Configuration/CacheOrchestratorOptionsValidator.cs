using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Validates core <see cref="CacheOrchestratorOptions"/> at startup (Data Cache settings,
/// instance references, and portable domain settings).
/// Provider-specific connection rules (e.g. Redis) are validated by the corresponding package.
/// </summary>
internal sealed class CacheOrchestratorOptionsValidator : IValidateOptions<CacheOrchestratorOptions>
{
    private readonly ILogger? _logger;

    public CacheOrchestratorOptionsValidator(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CacheOrchestratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.DataCacheInstances.Count == 0)
        {
            failures.Add("DataCacheInstances must contain at least one entry named 'default'.");
        }
        else
        {
            if (!options.DataCacheInstances.ContainsKey("default"))
                failures.Add("DataCacheInstances must contain an entry named 'default'.");

            // Data-cache L2 providers are owned by the Fusion/Hybrid packages (e.g. IFusionCacheBackendRegistrar).
            // AspNet host registrars only constrain OutputCache.Provider; unknown data-cache providers
            // fail later when the data-cache package resolves the backend by name.
            foreach ((string? instanceName, CacheOrchestratorOptions.DataCacheInstanceOptions? instanceOpts) in options.DataCacheInstances)
            {
                if (string.IsNullOrWhiteSpace(instanceOpts.Provider))
                {
                    failures.Add(
                        $"DataCacheInstances['{instanceName}'].Provider is required.");
                }
            }
        }

        ValidateDomainSettings("DomainDefaults", options.DomainDefaults, failures);

        string? defaultDataInstance = options.DomainDefaults.DataCache?.Instance;
        if (!string.IsNullOrWhiteSpace(defaultDataInstance)
            && !options.DataCacheInstances.ContainsKey(defaultDataInstance))
        {
            failures.Add(
                $"DomainDefaults: DataCache.Instance '{defaultDataInstance}' " +
                $"does not exist in DataCacheInstances.");
        }

        foreach ((string? domain, CacheOrchestratorOptions.DomainCacheSettings? settings) in options.Domains)
        {
            ValidateDomainKey(domain, failures);

            ValidateDomainSettings($"Domain '{domain}'", settings, failures);

            string? dataInstance = settings.DataCache?.Instance;
            if (!string.IsNullOrWhiteSpace(dataInstance) &&
                !options.DataCacheInstances.ContainsKey(dataInstance))
            {
                failures.Add(
                    $"Domain '{domain}': DataCache.Instance '{dataInstance}' " +
                    $"does not exist in DataCacheInstances.");
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
        ValidateNonNegSeconds(label, "DataCache.TtlSeconds", settings.DataCache?.TtlSeconds, failures);
    }

    private static void ValidateNonNegSeconds(
        string label,
        string propertyName,
        int? value,
        List<string> failures)
    {
        if (value is { } n && n < 0)
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
