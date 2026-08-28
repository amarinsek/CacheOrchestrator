using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Configuration;

internal sealed class CacheOrchestratorHttpOptionsValidator : IValidateOptions<CacheOrchestratorHttpOptions>
{
    private readonly HashSet<string>? _validOutputProviders;

    public CacheOrchestratorHttpOptionsValidator(IEnumerable<string>? validOutputProviders = null)
    {
        _validOutputProviders = validOutputProviders is null
            ? null
            : new HashSet<string>(validOutputProviders, StringComparer.OrdinalIgnoreCase);
    }

    public ValidateOptionsResult Validate(string? name, CacheOrchestratorHttpOptions options)
    {
        List<string> failures = [];
        if (_validOutputProviders is not null
            && !_validOutputProviders.Contains(options.OutputCache.Provider))
        {
            failures.Add(
                $"Unsupported OutputCache provider '{options.OutputCache.Provider}'. " +
                $"Registered providers: {string.Join(", ", _validOutputProviders)}.");
        }
        Validate("DomainDefaults", options.DomainDefaults, failures);
        foreach ((string domain, DomainHttpCacheSettings settings) in options.Domains)
        {
            Validate($"Domain '{domain}'", settings, failures);
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Validate(string label, DomainHttpCacheSettings settings, List<string> failures)
    {
        NonNegative(label, "OutputCache.TtlSeconds", settings.OutputCache?.TtlSeconds, failures);
        NonNegative(label, "ClientCache.TtlSeconds", settings.ClientCache?.TtlSeconds, failures);
        NonNegative(label, "ClientCache.TtlMinSeconds", settings.ClientCache?.TtlMinSeconds, failures);
        if (settings.ClientCache?.TtlSeconds is int ttl
            && settings.ClientCache.TtlMinSeconds is int min
            && min > ttl)
        {
            failures.Add($"{label}: ClientCache.TtlMinSeconds must be <= ClientCache.TtlSeconds.");
        }

        Allowlist(label, "VaryByHeaders", settings.VaryByHeaders, 8, failures);
        Allowlist(label, "VaryByCookies", settings.VaryByCookies, 8, failures);
        Allowlist(label, "VaryByQueryKeys", settings.VaryByQueryKeys, 32, failures);
        Allowlist(label, "IgnoreQueryKeys", settings.IgnoreQueryKeys, 32, failures);
        Allowlist(label, "VaryByAuthClaims", settings.VaryByAuthClaims, 16, failures);
        Allowlist(label, "AcceptNormalizationList", settings.AcceptNormalizationList, 16, failures);
        Allowlist(label, "AcceptLanguageNormalizationList", settings.AcceptLanguageNormalizationList, 16, failures);
        Allowlist(label, "OutputCache.EncodingNormalizationList", settings.OutputCache?.EncodingNormalizationList, 16, failures);

        if (settings.AuthBypassMode is AuthBypassMode auth && !Enum.IsDefined(auth))
        {
            failures.Add($"{label}: AuthBypassMode value '{auth}' is not defined.");
        }
        if (settings.OutputCache?.ETagMode is ETagMode etag && !Enum.IsDefined(etag))
        {
            failures.Add($"{label}: OutputCache.ETagMode value '{etag}' is not defined.");
        }
        if (settings.ClientCache?.Cacheability is ClientCacheability cacheability && !Enum.IsDefined(cacheability))
        {
            failures.Add($"{label}: ClientCache.Cacheability value '{cacheability}' is not defined.");
        }
    }

    private static void NonNegative(string label, string property, int? value, List<string> failures)
    {
        if (value < 0)
        {
            failures.Add($"{label}: {property} cannot be negative.");
        }
    }

    private static void Allowlist(string label, string property, string[]? values, int max, List<string> failures)
    {
        if (values is null)
        {
            return;
        }
        if (values.Length > max)
        {
            failures.Add($"{label}: {property} cannot contain more than {max} entries (got {values.Length}).");
        }
        for (int i = 0; i < values.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i]))
            {
                failures.Add($"{label}: {property}[{i}] must not be null or whitespace.");
            }
        }
    }
}
