using CacheOrchestrator.Admin;

namespace CacheOrchestrator.Admin.App.Services;

/// <summary>
/// Rule-based, read-only recommendations (plan §10). Informational labels on entities.
/// </summary>
public static class RecommendationHints
{
    /// <summary>Minimum requests before rate-based rules fire.</summary>
    public const long MinTraffic = 20;

    public static IReadOnlyList<AdminHintDto> ForDomain(
        AdminDomainStatsDto domain,
        AdminDomainConfigDto? config = null)
    {
        ArgumentNullException.ThrowIfNull(domain);
        List<AdminHintDto> hints = [];

        if (domain.Requests >= MinTraffic)
        {
            if (domain.Fc.HitRate is double fcHr && fcHr < 0.60)
            {
                hints.Add(Hint(
                    "Warning",
                    "low-fc-hit-rate",
                    $"FC layer hit rate {(fcHr * 100):0.#}% with {domain.Requests} requests — consider longer Fusion/Output TTL or fail-safe."));
            }

            if (domain.Oc.HitRate is double ocHr && ocHr < 0.60)
            {
                hints.Add(Hint(
                    "Warning",
                    "low-oc-hit-rate",
                    $"OC layer hit rate {(ocHr * 100):0.#}% — check Output TTL, auth bypass, or vary rules."));
            }

            if (domain.Fc.OriginShare is double origin && origin >= 0.25)
            {
                hints.Add(Hint(
                    "Warning",
                    "high-origin-share",
                    $"Origin/factory share {(origin * 100):0.#}% of requests — short TTL or frequent misses; consider soft/hard TTL and eager refresh."));
            }

            if (domain.Fc.StaleShare is double stale && stale >= 0.05)
            {
                hints.Add(Hint(
                    "Warning",
                    "elevated-stale",
                    $"Stale serves ~{(stale * 100):0.#}% of requests — factory failures or fail-safe in use; inspect factory timeouts."));
            }

            if (domain.Oc.HitRate is double hot && hot >= 0.98
                && config?.OutputCacheTtlSeconds is int ot && ot >= 3600)
            {
                hints.Add(Hint(
                    "Info",
                    "very-high-oc-hit-long-ttl",
                    $"OC hit rate {(hot * 100):0.#}% with Output TTL {ot}s — consider shorter TTL if fresher data is needed."));
            }

            if (domain.Invalidations >= 10
                && domain.Requests > 0
                && domain.Invalidations >= domain.Requests * 0.05)
            {
                hints.Add(Hint(
                    "Info",
                    "frequent-invalidations",
                    $"{domain.Invalidations} invalidations vs {domain.Requests} requests — entity-level invalidation / dynamic profile may fit better than domain-wide version bumps."));
            }
        }

        if (config is not null)
        {
            if (config.ClientTtlSeconds > 0
                && config.OutputCacheTtlSeconds > 0
                && config.ClientTtlSeconds > config.OutputCacheTtlSeconds * 2)
            {
                hints.Add(Hint(
                    "Info",
                    "client-ttl-gt-output",
                    $"Client TTL ({config.ClientTtlSeconds}s) ≫ Output TTL ({config.OutputCacheTtlSeconds}s) — align the ratio to avoid stale browser cache."));
            }

            if (string.Equals(config.SchedulePhase, "hold", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.SchedulePhase, "approaching", StringComparison.OrdinalIgnoreCase))
            {
                hints.Add(Hint(
                    "Info",
                    "schedule-phase",
                    $"Client Cache Schedule phase is '{config.SchedulePhase}' — verify ScheduledUpdateUtc is still correct."));
            }
        }

        if (domain.InstanceSpread?.OcHitShare is { SampleCount: >= 2, Stdev: double sd }
            && sd >= 0.15)
        {
            hints.Add(Hint(
                "Warning",
                "instance-oc-hit-spread",
                $"OC hit share varies across instances (stdev {(sd * 100):0.#}%) — check L1 consistency / uneven traffic."));
        }

        return hints;
    }

    public static IReadOnlyList<AdminHintDto> ForEndpoint(AdminEndpointStatsDto ep)
    {
        ArgumentNullException.ThrowIfNull(ep);
        List<AdminHintDto> hints = [];

        if (ep.Requests >= MinTraffic)
        {
            if (ep.Fc.HitRate is double fcHr && fcHr < 0.60 && (ep.Fc.LayerSampleSize >= MinTraffic / 2))
            {
                hints.Add(Hint(
                    "Warning",
                    "low-fc-hit-rate",
                    $"FC layer hit rate {(fcHr * 100):0.#}% on this route — review Fusion TTL or key cardinality."));
            }

            if (ep.Fc.OriginShare is double origin && origin >= 0.25)
            {
                hints.Add(Hint(
                    "Warning",
                    "high-origin-share",
                    $"Origin share {(origin * 100):0.#}% — factory runs often; consider longer soft TTL or eager refresh."));
            }

            if (ep.Fc.Stale > 0 && ep.Fc.StaleShare is double ss && ss >= 0.05)
            {
                hints.Add(Hint(
                    "Warning",
                    "elevated-stale",
                    $"Stale share {(ss * 100):0.#}% — fail-safe serving after factory issues."));
            }

            if (ep.Oc.HitShare is double ocHit && ocHit >= 0.95
                && ep.Fc.MissRate is double fmr && fmr >= 0.99
                && ep.Fc.LayerSampleSize is > 0 and < AdminStatsMath.LowSampleThreshold)
            {
                hints.Add(Hint(
                    "Info",
                    "fc-miss-rate-vs-oc-share",
                    "FC miss rate looks high only on rare OC misses — prefer Origin/FC miss share of requests, not layer miss rate alone."));
            }

            if (ep.InstanceSpread?.OriginShare is { SampleCount: >= 2, Stdev: double sd } && sd >= 0.15)
            {
                hints.Add(Hint(
                    "Warning",
                    "instance-origin-spread",
                    $"Origin share differs across instances (stdev {(sd * 100):0.#}%)."));
            }
        }

        return hints;
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

    private static AdminHintDto Hint(string severity, string code, string message) =>
        new() { Severity = severity, Code = code, Message = message };
}
