using CacheOrchestrator.Admin;

namespace CacheOrchestrator.Admin.App.Services;

/// <summary>
/// Read-only operator hints. Warning and Critical mean something is wrong
/// (too much origin traffic, factory throwing, fail-safe covering failures, forgotten hold, or instance drift).
/// Info marks a temporary or special but expected state (approaching, recent hold, runtime overlay).
/// Layer hit rates are not used: a 0% Fusion rate with a healthy origin share is normal
/// when Output Cache absorbs most requests.
/// </summary>
public static class RecommendationHints
{
    public const long MinTraffic = 20;

    /// <summary>Minimum factory runs before a failure-rate rule fires.</summary>
    public const long MinFactoryRuns = 10;

    /// <summary>Factory share of all requests that warrants a warning.</summary>
    public const double OriginShareWarning = 0.25;

    /// <summary>Factory share that dominates the pipeline.</summary>
    public const double OriginShareCritical = 0.50;

    /// <summary>Stale share of all requests that means fail-safe is covering repeated factory trouble.</summary>
    public const double StaleShareWarning = 0.10;

    /// <summary>FactoryFailures / FactoryRuns that warrants a warning.</summary>
    public const double FactoryFailureWarning = 0.10;

    /// <summary>FactoryFailures / FactoryRuns that means the origin is mostly failing.</summary>
    public const double FactoryFailureCritical = 0.50;

    /// <summary>Hold longer than this after ScheduledUpdateUtc is no longer a fresh cutover.</summary>
    public const int HoldLingeringHours = 24;

    public static IReadOnlyList<AdminHintDto> ForDomain(
        AdminDomainStatsDto domain,
        AdminDomainConfigDto? config = null)
    {
        ArgumentNullException.ThrowIfNull(domain);
        List<AdminHintDto> hints = [];

        if (domain.Requests >= MinTraffic)
        {
            if (domain.Fc.OriginShare is double origin && origin >= OriginShareWarning)
            {
                string severity = origin >= OriginShareCritical ? "Critical" : "Warning";
                string code = origin >= OriginShareCritical ? "critical-origin-share" : "high-origin-share";
                hints.Add(Hint(
                    severity,
                    code,
                    $"Origin/factory is {(origin * 100):0.#}% of {domain.Requests} requests — " +
                    "the cache is not absorbing traffic. Check Fusion/Output TTL, key cardinality, and invalidation."));
            }

            if (domain.Fc.StaleShare is double stale && stale >= StaleShareWarning)
            {
                hints.Add(Hint(
                    "Warning",
                    "elevated-stale",
                    $"Stale serves {(stale * 100):0.#}% of requests — fail-safe is covering factory failures. Inspect timeouts and origin health."));
            }

            if (domain.Invalidations >= 10
                && domain.Requests > 0
                && domain.Invalidations >= domain.Requests * 0.05)
            {
                hints.Add(Hint(
                    "Info",
                    "frequent-invalidations",
                    $"{domain.Invalidations} invalidations vs {domain.Requests} requests — " +
                    "entity-level invalidation may fit better than domain-wide Version bumps."));
            }
        }

        AddFactoryFailureHints(hints, domain.Fc, config);

        string? phase = ResolvePhase(domain.SchedulePhase, config?.SchedulePhase);
        bool hasSchedule = config?.ScheduledUpdateUtc is not null || phase is not null;

        AddScheduleHints(hints, phase, config);

        if (config is not null)
        {
            if (!hasSchedule
                && config.ClientTtlSeconds > 0
                && config.OutputCacheTtlSeconds > 0
                && config.ClientTtlSeconds > config.OutputCacheTtlSeconds * 2)
            {
                hints.Add(Hint(
                    "Info",
                    "client-ttl-gt-output",
                    $"Client TTL ({config.ClientTtlSeconds}s) is much larger than Output TTL ({config.OutputCacheTtlSeconds}s) — " +
                    "browsers can stay stale after the server cache expires. Use a schedule or align the TTLs."));
            }

            if (hasSchedule
                && config.ClientTtlSeconds > 0
                && config.ClientTtlMinSeconds >= config.ClientTtlSeconds)
            {
                hints.Add(Hint(
                    "Info",
                    "schedule-flat",
                    $"Client min TTL ({config.ClientTtlMinSeconds}s) is not below max ({config.ClientTtlSeconds}s) — " +
                    "the schedule cannot ramp. Lower ClientTtlMinSeconds or raise ClientTtlSeconds."));
            }

            if (config.FusionCacheHardTtlSeconds > 0
                && config.FusionCacheSoftTtlSeconds > config.FusionCacheHardTtlSeconds)
            {
                hints.Add(Hint(
                    "Warning",
                    "fusion-hard-lt-soft",
                    $"Fusion hard TTL ({config.FusionCacheHardTtlSeconds}s) is shorter than soft ({config.FusionCacheSoftTtlSeconds}s) — " +
                    "hard wins. Raise hard or lower soft."));
            }
        }

        if (HasRuntimeOverlay(domain, config))
        {
            hints.Add(Hint(
                "Info",
                "runtime-override",
                "A runtime overlay is in effect (Version and/or TTLs) — persist it in configuration if it should survive process restart."));
        }

        if (domain.InstanceSpread?.OcHitShare is { SampleCount: >= 2, Stdev: double sd }
            && sd >= 0.15)
        {
            hints.Add(Hint(
                "Warning",
                "instance-oc-hit-spread",
                $"OC hit share varies across instances (stdev {(sd * 100):0.#}%) — InMemory OC is not shared, or traffic is uneven."));
        }

        return hints;
    }

    public static IReadOnlyList<AdminHintDto> ForEndpoint(AdminEndpointStatsDto ep)
    {
        ArgumentNullException.ThrowIfNull(ep);
        List<AdminHintDto> hints = [];

        if (ep.Requests >= MinTraffic)
        {
            if (ep.Fc.OriginShare is double origin && origin >= OriginShareWarning)
            {
                string severity = origin >= OriginShareCritical ? "Critical" : "Warning";
                string code = origin >= OriginShareCritical ? "critical-origin-share" : "high-origin-share";
                hints.Add(Hint(
                    severity,
                    code,
                    $"Origin is {(origin * 100):0.#}% of requests on this route — factory runs too often."));
            }

            if (ep.Fc.Stale > 0 && ep.Fc.StaleShare is double ss && ss >= StaleShareWarning)
            {
                hints.Add(Hint(
                    "Warning",
                    "elevated-stale",
                    $"Stale share {(ss * 100):0.#}% — fail-safe after factory issues."));
            }

            if (ep.InstanceSpread?.OriginShare is { SampleCount: >= 2, Stdev: double sd } && sd >= 0.15)
            {
                hints.Add(Hint(
                    "Warning",
                    "instance-origin-spread",
                    $"Origin share differs across instances (stdev {(sd * 100):0.#}%)."));
            }
        }

        AddFactoryFailureHints(hints, ep.Fc, config: null);

        return hints;
    }

    private static void AddFactoryFailureHints(
        List<AdminHintDto> hints,
        AdminFusionLayerDto fc,
        AdminDomainConfigDto? config)
    {
        if (fc.FactoryRuns < MinFactoryRuns || fc.FactoryFailures <= 0)
            return;

        double failRate = (double)fc.FactoryFailures / fc.FactoryRuns;
        if (failRate < FactoryFailureWarning)
            return;

        bool failSafeOff = config is { FusionCacheFailSafeSeconds: <= 0 };
        string failSafeNote = failSafeOff
            ? " Fail-safe is off, so these misses are not covered by stale."
            : " Inspect origin errors; fail-safe may be covering them as stale.";

        if (failRate >= FactoryFailureCritical)
        {
            hints.Add(Hint(
                "Critical",
                "critical-factory-failures",
                $"Factory failed {(failRate * 100):0.#}% of {fc.FactoryRuns} runs.{failSafeNote}"));
        }
        else
        {
            hints.Add(Hint(
                "Warning",
                "factory-failures",
                $"Factory failed {(failRate * 100):0.#}% of {fc.FactoryRuns} runs.{failSafeNote}"));
        }
    }

    private static void AddScheduleHints(
        List<AdminHintDto> hints,
        string? phase,
        AdminDomainConfigDto? config)
    {
        if (string.Equals(phase, "approaching", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add(Hint(
                "Info",
                "schedule-approaching",
                "Client Cache Schedule is approaching the cutover — client max-age is ramping down. This is expected."));
            return;
        }

        if (!string.Equals(phase, "hold", StringComparison.OrdinalIgnoreCase))
            return;

        if (config?.ScheduledUpdateUtc is DateTimeOffset scheduled
            && DateTimeOffset.UtcNow - scheduled >= TimeSpan.FromHours(HoldLingeringHours))
        {
            hints.Add(Hint(
                "Warning",
                "schedule-hold-lingering",
                $"Client Cache Schedule has been in hold since {scheduled:u} — " +
                "set the next ScheduledUpdateUtc or clear the schedule so clients can return to a long max-age."));
            return;
        }

        hints.Add(Hint(
            "Info",
            "schedule-phase",
            "Client Cache Schedule is in hold — set the next ScheduledUpdateUtc when the cutover is done."));
    }

    private static string? ResolvePhase(string? domainPhase, string? configPhase)
    {
        string? phase = FirstNonEmpty(configPhase, domainPhase);
        if (string.IsNullOrEmpty(phase)
            || string.Equals(phase, "n/a", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return phase;
    }

    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrEmpty(a) ? a : !string.IsNullOrEmpty(b) ? b : null;

    private static bool HasRuntimeOverlay(AdminDomainStatsDto domain, AdminDomainConfigDto? config)
    {
        if (domain.VersionIsRuntimeOverride)
            return true;
        if (config is null)
            return false;
        if (config.VersionIsRuntimeOverride)
            return true;

        AdminRuntimeOverrideFlagsDto? flags = config.RuntimeOverrides;
        return flags is not null
            && (flags.Version
                || flags.OutputCacheTtl
                || flags.FusionCacheSoftTtl
                || flags.FusionCacheHardTtl
                || flags.FusionCacheFailSafe
                || flags.ClientTtl
                || flags.ClientTtlMin);
    }

    public static AdminDomainStatsDto WithHints(
        AdminDomainStatsDto domain,
        AdminDomainConfigDto? config = null) =>
        new()
        {
            Name = domain.Name,
            InstanceId = domain.InstanceId,
            Version = domain.Version,
            VersionIsRuntimeOverride = domain.VersionIsRuntimeOverride,
            SchedulePhase = domain.SchedulePhase,
            LastInvalidationUtc = domain.LastInvalidationUtc,
            Invalidations = domain.Invalidations,
            Requests = domain.Requests,
            Oc = domain.Oc,
            Fc = domain.Fc,
            Pipeline = domain.Pipeline,
            Endpoints = domain.Endpoints.Select(e => WithHints(e)).ToArray(),
            ByInstance = domain.ByInstance?
                .Select(b => WithHints(b, config))
                .ToArray(),
            InstanceSpread = domain.InstanceSpread,
            Hints = ForDomain(domain, config)
        };

    public static AdminEndpointStatsDto WithHints(AdminEndpointStatsDto ep) =>
        new()
        {
            Route = ep.Route,
            InstanceId = ep.InstanceId,
            ConfiguredDomain = ep.ConfiguredDomain,
            Requests = ep.Requests,
            Oc = ep.Oc,
            Fc = ep.Fc,
            Pipeline = ep.Pipeline,
            ByInstance = ep.ByInstance?.Select(WithHints).ToArray(),
            InstanceSpread = ep.InstanceSpread,
            Hints = ForEndpoint(ep)
        };

    public static AdminHintSummaryDto Summarize(IEnumerable<AdminHintDto> hints)
    {
        int info = 0, warning = 0, critical = 0;
        foreach (AdminHintDto h in hints)
        {
            switch (h.Severity)
            {
                case "Critical":
                    critical++;
                    break;
                case "Warning":
                    warning++;
                    break;
                default:
                    info++;
                    break;
            }
        }

        return new AdminHintSummaryDto
        {
            Info = info,
            Warning = warning,
            Critical = critical
        };
    }

    public static IReadOnlyList<AdminHintDto> CollectFromStats(
        IReadOnlyList<AdminDomainStatsDto> domains,
        IReadOnlyList<AdminEndpointStatsDto>? endpoints = null)
    {
        List<AdminHintDto> list = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        void addRange(IEnumerable<AdminHintDto>? hints)
        {
            if (hints is null)
                return;
            foreach (AdminHintDto h in hints)
            {
                string key = h.Severity + "|" + h.Code + "|" + h.Message;
                if (seen.Add(key))
                    list.Add(h);
            }
        }

        foreach (AdminDomainStatsDto d in domains)
        {
            addRange(d.Hints);
            foreach (AdminEndpointStatsDto e in d.Endpoints)
                addRange(e.Hints);
        }

        if (endpoints is not null)
        {
            foreach (AdminEndpointStatsDto e in endpoints)
                addRange(e.Hints);
        }

        return list;
    }

    private static AdminHintDto Hint(string severity, string code, string message) =>
        new() { Severity = severity, Code = code, Message = message };
}
