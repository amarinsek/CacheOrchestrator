using CacheOrchestrator.Admin;

namespace CacheOrchestrator.Admin.App.Services;

/// <summary>
/// Pure aggregation of Local Admin stats snapshots across instances.
/// </summary>
public static class StatsAggregator
{
    /// <summary>Merges domain counters (and nested endpoints) by domain name.</summary>
    public static IReadOnlyList<AdminDomainStatsDto> MergeDomains(IReadOnlyList<AdminLiveStatsSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        Dictionary<string, MutableDomain> map = new(StringComparer.Ordinal);

        foreach (AdminLiveStatsSnapshot snap in snapshots)
        {
            foreach (AdminDomainStatsDto domain in snap.Domains)
            {
                if (!map.TryGetValue(domain.Name, out MutableDomain? acc))
                {
                    acc = new MutableDomain(domain.Name);
                    map[domain.Name] = acc;
                }

                acc.Add(domain);
            }
        }

        return map.Values
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => d.ToDto())
            .ToArray();
    }

    /// <summary>Merges unassigned endpoints by route key.</summary>
    public static IReadOnlyList<AdminEndpointStatsDto> MergeUnassignedEndpoints(
        IReadOnlyList<AdminLiveStatsSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        Dictionary<string, MutableEndpoint> map = new(StringComparer.Ordinal);
        foreach (AdminLiveStatsSnapshot snap in snapshots)
        {
            foreach (AdminEndpointStatsDto ep in snap.UnassignedEndpoints)
            {
                if (!map.TryGetValue(ep.Route, out MutableEndpoint? acc))
                {
                    acc = new MutableEndpoint(ep.Route, ep.ConfiguredDomain);
                    map[ep.Route] = acc;
                }

                acc.Add(ep);
            }
        }

        return map.Values
            .OrderBy(e => e.Route, StringComparer.Ordinal)
            .Select(e => e.ToDto())
            .ToArray();
    }

    private sealed class MutableDomain(string name)
    {
        public string Name { get; } = name;
        public string? Version { get; private set; }
        public bool VersionIsRuntimeOverride { get; private set; }
        public string? SchedulePhase { get; private set; }
        public DateTimeOffset? LastInvalidationUtc { get; private set; }
        public long Invalidations { get; private set; }

        private long _ocHits, _ocMisses, _ocBypass;
        private long _fcHits, _fcMisses, _fcStale, _fcBypass, _fcRuns, _fcFails;
        private readonly Dictionary<string, MutableEndpoint> _endpoints = new(StringComparer.Ordinal);

        public void Add(AdminDomainStatsDto d)
        {
            Version ??= d.Version;
            VersionIsRuntimeOverride |= d.VersionIsRuntimeOverride;
            SchedulePhase ??= d.SchedulePhase;
            Invalidations += d.Invalidations;
            if (d.LastInvalidationUtc is DateTimeOffset t
                && (LastInvalidationUtc is null || t > LastInvalidationUtc))
            {
                LastInvalidationUtc = t;
            }

            _ocHits += d.Oc.Hits;
            _ocMisses += d.Oc.Misses;
            _ocBypass += d.Oc.Bypass;
            _fcHits += d.Fc.Hits;
            _fcMisses += d.Fc.Misses;
            _fcStale += d.Fc.Stale;
            _fcBypass += d.Fc.Bypass;
            _fcRuns += d.Fc.FactoryRuns;
            _fcFails += d.Fc.FactoryFailures;

            foreach (AdminEndpointStatsDto ep in d.Endpoints)
            {
                if (!_endpoints.TryGetValue(ep.Route, out MutableEndpoint? acc))
                {
                    acc = new MutableEndpoint(ep.Route, ep.ConfiguredDomain ?? Name);
                    _endpoints[ep.Route] = acc;
                }

                acc.Add(ep);
            }
        }

        public AdminDomainStatsDto ToDto() =>
            new()
            {
                Name = Name,
                Version = Version ?? string.Empty,
                VersionIsRuntimeOverride = VersionIsRuntimeOverride,
                SchedulePhase = SchedulePhase,
                LastInvalidationUtc = LastInvalidationUtc,
                Invalidations = Invalidations,
                Oc = Layer(_ocHits, _ocMisses, _ocBypass),
                Fc = Fusion(_fcHits, _fcMisses, _fcStale, _fcBypass, _fcRuns, _fcFails),
                Endpoints = _endpoints.Values
                    .OrderBy(e => e.Route, StringComparer.Ordinal)
                    .Select(e => e.ToDto())
                    .ToArray()
            };
    }

    private sealed class MutableEndpoint(string route, string? configuredDomain)
    {
        public string Route { get; } = route;
        public string? ConfiguredDomain { get; private set; } = configuredDomain;

        private long _ocHits, _ocMisses, _ocBypass;
        private long _fcHits, _fcMisses, _fcStale, _fcBypass, _fcRuns, _fcFails;

        public void Add(AdminEndpointStatsDto ep)
        {
            ConfiguredDomain ??= ep.ConfiguredDomain;
            _ocHits += ep.Oc.Hits;
            _ocMisses += ep.Oc.Misses;
            _ocBypass += ep.Oc.Bypass;
            _fcHits += ep.Fc.Hits;
            _fcMisses += ep.Fc.Misses;
            _fcStale += ep.Fc.Stale;
            _fcBypass += ep.Fc.Bypass;
            _fcRuns += ep.Fc.FactoryRuns;
            _fcFails += ep.Fc.FactoryFailures;
        }

        public AdminEndpointStatsDto ToDto() =>
            new()
            {
                Route = Route,
                ConfiguredDomain = ConfiguredDomain,
                Oc = Layer(_ocHits, _ocMisses, _ocBypass),
                Fc = Fusion(_fcHits, _fcMisses, _fcStale, _fcBypass, _fcRuns, _fcFails)
            };
    }

    private static AdminLayerDto Layer(long hits, long misses, long bypass) =>
        new()
        {
            Hits = hits,
            Misses = misses,
            Bypass = bypass,
            HitRate = HitRate(hits, misses)
        };

    private static AdminFusionLayerDto Fusion(
        long hits,
        long misses,
        long stale,
        long bypass,
        long runs,
        long fails) =>
        new()
        {
            Hits = hits,
            Misses = misses,
            Stale = stale,
            Bypass = bypass,
            FactoryRuns = runs,
            FactoryFailures = fails,
            HitRate = HitRate(hits, misses)
        };

    private static double? HitRate(long hits, long misses)
    {
        long total = hits + misses;
        return total <= 0 ? null : (double)hits / total;
    }
}
