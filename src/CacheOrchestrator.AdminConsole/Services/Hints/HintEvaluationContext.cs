using CacheOrchestrator.Admin;

namespace CacheOrchestrator.AdminConsole.Services.Hints;

/// <summary>
/// Read-only facts for hint rules. Rules must not call HTTP or PromQL; only this model.
/// </summary>
public sealed class HintEvaluationContext
{
    public required DateTimeOffset NowUtc { get; init; }

    /// <summary>Domain under evaluation (domain-scoped rules).</summary>
    public AdminDomainStatsDto? Domain { get; init; }

    /// <summary>Endpoint under evaluation (endpoint-scoped rules).</summary>
    public AdminEndpointStatsDto? Endpoint { get; init; }

    /// <summary>Effective config for <see cref="Domain"/> (or endpoint's configured domain).</summary>
    public AdminDomainConfigDto? Config { get; init; }

    /// <summary>All domain configs by name (rarely needed in declarative paths).</summary>
    public IReadOnlyDictionary<string, AdminDomainConfigDto> ConfigByName { get; init; } =
        new Dictionary<string, AdminDomainConfigDto>(StringComparer.Ordinal);

    /// <summary>
    /// Resolves a dotted path used by declarative rules (e.g. <c>domain.fc.factoryShare</c>).
    /// Returns <see langword="null"/> when missing (comparisons treat null as not matching).
    /// </summary>
    public object? ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string[] parts = path.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        object? current = parts[0].ToLowerInvariant() switch
        {
            "domain" => Domain,
            "endpoint" => Endpoint,
            "config" => Config,
            "now" => NowUtc,
            _ => null
        };

        if (current is null)
            return null;

        if (parts.Length == 1)
            return current;

        // Special computed roots
        if (parts[0].Equals("now", StringComparison.OrdinalIgnoreCase))
            return ResolveNow(parts.AsSpan(1));

        for (int i = 1; i < parts.Length && current is not null; i++)
            current = ResolveMember(current, parts[i]);

        return current;
    }

    private static object? ResolveNow(ReadOnlySpan<string> rest)
    {
        if (rest.Length == 0)
            return null;
        // now.utc is the DateTimeOffset itself
        if (rest[0].Equals("utc", StringComparison.OrdinalIgnoreCase) && rest.Length == 1)
            return null; // caller passes NowUtc via path "now" only — use holdAgeHours on config instead
        return null;
    }

    private static object? ResolveMember(object target, string name)
    {
        // Computed helpers on domain
        if (target is AdminDomainStatsDto domain)
        {
            if (name.Equals("hasSchedule", StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrEmpty(domain.SchedulePhase)
                    && !string.Equals(domain.SchedulePhase, "n/a", StringComparison.OrdinalIgnoreCase);
            if (name.Equals("factoryFailureRate", StringComparison.OrdinalIgnoreCase))
                return domain.Fc.FactoryRuns > 0
                    ? (double)domain.Fc.FactoryFailures / domain.Fc.FactoryRuns
                    : null;
            if (name.Equals("invalidationShare", StringComparison.OrdinalIgnoreCase))
                return domain.Requests > 0
                    ? (double)domain.Invalidations / domain.Requests
                    : null;
        }

        if (target is AdminEndpointStatsDto ep)
        {
            if (name.Equals("factoryFailureRate", StringComparison.OrdinalIgnoreCase))
                return ep.Fc.FactoryRuns > 0
                    ? (double)ep.Fc.FactoryFailures / ep.Fc.FactoryRuns
                    : null;
        }

        if (target is AdminDomainConfigDto config)
        {
            if (name.Equals("hasSchedule", StringComparison.OrdinalIgnoreCase))
                return config.ScheduledUpdateUtc is not null
                    || (!string.IsNullOrEmpty(config.SchedulePhase)
                        && !string.Equals(config.SchedulePhase, "n/a", StringComparison.OrdinalIgnoreCase));
            if (name.Equals("holdAgeHours", StringComparison.OrdinalIgnoreCase))
            {
                if (config.ScheduledUpdateUtc is not DateTimeOffset scheduled)
                    return null;
                return (DateTimeOffset.UtcNow - scheduled).TotalHours;
            }
            if (name.Equals("clientTtlOverOutputRatio", StringComparison.OrdinalIgnoreCase))
            {
                if (config.OutputCacheTtlSeconds <= 0)
                    return null;
                return (double)config.ClientTtlSeconds / config.OutputCacheTtlSeconds;
            }
            if (name.Equals("clientTtlCannotRamp", StringComparison.OrdinalIgnoreCase))
                return config.ClientTtlSeconds > 0
                    && config.ClientTtlMinSeconds >= config.ClientTtlSeconds;
            if (name.Equals("fusionHardLtSoft", StringComparison.OrdinalIgnoreCase))
                return config.FusionCacheHardTtlSeconds > 0
                    && config.FusionCacheSoftTtlSeconds > config.FusionCacheHardTtlSeconds;
        }

        if (target is AdminFusionLayerDto fc)
        {
            if (name.Equals("factoryFailureRate", StringComparison.OrdinalIgnoreCase))
                return fc.FactoryRuns > 0 ? (double)fc.FactoryFailures / fc.FactoryRuns : null;
        }

        if (target is AdminShareSpreadDto spread)
        {
            // domain.instanceSpread.ocHitShare → nested; stdev via .stdev
        }

        System.Reflection.PropertyInfo? prop = target.GetType()
            .GetProperties()
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (prop is null)
            return null;

        return prop.GetValue(target);
    }
}
