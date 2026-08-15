using CacheOrchestrator.Admin;

namespace CacheOrchestrator.Admin.App.Services;

/// <summary>
/// Pure aggregation of Local Admin stats snapshots across instances.
/// Counters are summed; rates/shares recomputed from sums; optional by-instance spreads.
/// </summary>
public static class StatsAggregator
{
    /// <summary>Merges domain counters by domain name.</summary>
    /// <param name="includeByInstance">When true, attach per-instance rows and spreads.</param>
    public static IReadOnlyList<AdminDomainStatsDto> MergeDomains(
        IReadOnlyList<AdminLiveStatsSnapshot> snapshots,
        bool includeByInstance = false)
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

                acc.Add(domain, snap.InstanceId);
            }
        }

        return map.Values
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => d.ToDto(includeByInstance))
            .ToArray();
    }

    /// <summary>Merges endpoints by route (cluster-wide).</summary>
    public static IReadOnlyList<AdminEndpointStatsDto> MergeEndpoints(
        IReadOnlyList<AdminLiveStatsSnapshot> snapshots,
        bool includeByInstance = false)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        Dictionary<string, MutableEndpoint> map = new(StringComparer.Ordinal);
        foreach (AdminLiveStatsSnapshot snap in snapshots)
        {
            IEnumerable<AdminEndpointStatsDto> eps = snap.Endpoints.Count > 0
                ? snap.Endpoints
                : snap.Domains.SelectMany(d => d.Endpoints).Concat(snap.UnassignedEndpoints);

            foreach (AdminEndpointStatsDto ep in eps)
            {
                if (!map.TryGetValue(ep.Route, out MutableEndpoint? acc))
                {
                    acc = new MutableEndpoint(ep.Route, ep.ConfiguredDomain);
                    map[ep.Route] = acc;
                }

                acc.Add(ep, snap.InstanceId);
            }
        }

        return map.Values
            .OrderBy(e => e.Route, StringComparer.Ordinal)
            .Select(e => e.ToDto(includeByInstance))
            .ToArray();
    }

    /// <summary>Merges unassigned endpoints by route key.</summary>
    public static IReadOnlyList<AdminEndpointStatsDto> MergeUnassignedEndpoints(
        IReadOnlyList<AdminLiveStatsSnapshot> snapshots,
        bool includeByInstance = false)
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

                acc.Add(ep, snap.InstanceId);
            }
        }

        return map.Values
            .OrderBy(e => e.Route, StringComparer.Ordinal)
            .Select(e => e.ToDto(includeByInstance))
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
        private readonly List<AdminDomainStatsDto> _instanceRows = [];

        public void Add(AdminDomainStatsDto d, string instanceId)
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

            _instanceRows.Add(new AdminDomainStatsDto
            {
                Name = d.Name,
                InstanceId = d.InstanceId ?? instanceId,
                Version = d.Version,
                VersionIsRuntimeOverride = d.VersionIsRuntimeOverride,
                SchedulePhase = d.SchedulePhase,
                LastInvalidationUtc = d.LastInvalidationUtc,
                Invalidations = d.Invalidations,
                Requests = d.Requests,
                Oc = d.Oc,
                Fc = d.Fc,
                Pipeline = d.Pipeline,
                Endpoints = d.Endpoints
            });

            foreach (AdminEndpointStatsDto ep in d.Endpoints)
            {
                if (!_endpoints.TryGetValue(ep.Route, out MutableEndpoint? acc))
                {
                    acc = new MutableEndpoint(ep.Route, ep.ConfiguredDomain ?? Name);
                    _endpoints[ep.Route] = acc;
                }

                acc.Add(ep, instanceId);
            }
        }

        public AdminDomainStatsDto ToDto(bool includeByInstance)
        {
            (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) =
                AdminStatsMath.BuildAll(
                    _ocHits, _ocMisses, _ocBypass,
                    _fcHits, _fcMisses, _fcStale, _fcBypass,
                    _fcRuns, _fcFails);

            List<AdminEndpointStatsDto> endpoints = _endpoints.Values
                .OrderBy(e => e.Route, StringComparer.Ordinal)
                .Select(e => e.ToDto(includeByInstance))
                .ToList();

            AdminInstanceSpreadDto? spread = null;
            IReadOnlyList<AdminDomainStatsDto>? byInstance = null;
            if (includeByInstance && _instanceRows.Count > 0)
            {
                byInstance = _instanceRows
                    .OrderBy(r => r.InstanceId, StringComparer.Ordinal)
                    .ToArray();
                spread = BuildSpread(_instanceRows.Select(r => r));
            }

            return new AdminDomainStatsDto
            {
                Name = Name,
                Version = Version ?? string.Empty,
                VersionIsRuntimeOverride = VersionIsRuntimeOverride,
                SchedulePhase = SchedulePhase,
                LastInvalidationUtc = LastInvalidationUtc,
                Invalidations = Invalidations,
                Requests = requests,
                Oc = oc,
                Fc = fc,
                Pipeline = pipeline,
                Endpoints = endpoints,
                ByInstance = byInstance,
                InstanceSpread = spread
            };
        }
    }

    private sealed class MutableEndpoint(string route, string? configuredDomain)
    {
        public string Route { get; } = route;
        public string? ConfiguredDomain { get; private set; } = configuredDomain;

        private long _ocHits, _ocMisses, _ocBypass;
        private long _fcHits, _fcMisses, _fcStale, _fcBypass, _fcRuns, _fcFails;
        private readonly List<AdminEndpointStatsDto> _instanceRows = [];

        public void Add(AdminEndpointStatsDto ep, string instanceId)
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

            _instanceRows.Add(new AdminEndpointStatsDto
            {
                Route = ep.Route,
                InstanceId = ep.InstanceId ?? instanceId,
                ConfiguredDomain = ep.ConfiguredDomain ?? ConfiguredDomain,
                Requests = ep.Requests,
                Oc = ep.Oc,
                Fc = ep.Fc,
                Pipeline = ep.Pipeline
            });
        }

        public AdminEndpointStatsDto ToDto(bool includeByInstance)
        {
            (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) =
                AdminStatsMath.BuildAll(
                    _ocHits, _ocMisses, _ocBypass,
                    _fcHits, _fcMisses, _fcStale, _fcBypass,
                    _fcRuns, _fcFails);

            IReadOnlyList<AdminEndpointStatsDto>? byInstance = null;
            AdminInstanceSpreadDto? spread = null;
            if (includeByInstance && _instanceRows.Count > 0)
            {
                byInstance = _instanceRows
                    .OrderBy(r => r.InstanceId, StringComparer.Ordinal)
                    .ToArray();
                spread = BuildSpread(_instanceRows);
            }

            return new AdminEndpointStatsDto
            {
                Route = Route,
                ConfiguredDomain = ConfiguredDomain,
                Requests = requests,
                Oc = oc,
                Fc = fc,
                Pipeline = pipeline,
                ByInstance = byInstance,
                InstanceSpread = spread
            };
        }
    }

    private static AdminInstanceSpreadDto BuildSpread(IEnumerable<AdminDomainStatsDto> rows) =>
        new()
        {
            OcHitShare = AdminStatsMath.Spread(rows.Select(r => r.Oc.HitShare)),
            FcHitShare = AdminStatsMath.Spread(rows.Select(r => r.Fc.HitShare)),
            FcMissShare = AdminStatsMath.Spread(rows.Select(r => r.Fc.MissShare)),
            FactoryShare = AdminStatsMath.Spread(rows.Select(r => r.Fc.FactoryShare)),
            OcHitRate = AdminStatsMath.Spread(rows.Select(r => r.Oc.HitRate)),
            FcHitRate = AdminStatsMath.Spread(rows.Select(r => r.Fc.HitRate))
        };

    private static AdminInstanceSpreadDto BuildSpread(IEnumerable<AdminEndpointStatsDto> rows) =>
        new()
        {
            OcHitShare = AdminStatsMath.Spread(rows.Select(r => r.Oc.HitShare)),
            FcHitShare = AdminStatsMath.Spread(rows.Select(r => r.Fc.HitShare)),
            FcMissShare = AdminStatsMath.Spread(rows.Select(r => r.Fc.MissShare)),
            FactoryShare = AdminStatsMath.Spread(rows.Select(r => r.Fc.FactoryShare)),
            OcHitRate = AdminStatsMath.Spread(rows.Select(r => r.Oc.HitRate)),
            FcHitRate = AdminStatsMath.Spread(rows.Select(r => r.Fc.HitRate))
        };
}
