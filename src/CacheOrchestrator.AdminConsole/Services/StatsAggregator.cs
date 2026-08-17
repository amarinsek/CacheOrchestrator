using CacheOrchestrator.Admin;

namespace CacheOrchestrator.AdminConsole.Services;

/// <summary>
/// Aggregates Local Admin <strong>raw</strong> stats (v2) across instances.
/// Sums counters; recomputes rates/shares and impact KPIs in the Console.
/// </summary>
public static class StatsAggregator
{
    /// <summary>Merges domain counters by domain name from raw v2 snapshots.</summary>
    public static IReadOnlyList<AdminDomainStatsDto> MergeDomains(
        IReadOnlyList<AdminLiveStatsRawSnapshot> snapshots,
        bool includeByInstance = false)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        Dictionary<string, MutableDomain> map = new(StringComparer.Ordinal);

        foreach (AdminLiveStatsRawSnapshot snap in snapshots)
        {
            foreach (AdminDomainCountersDto domain in snap.Domains)
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

    /// <summary>Merges endpoints by route (cluster-wide) from raw v2 snapshots.</summary>
    public static IReadOnlyList<AdminEndpointStatsDto> MergeEndpoints(
        IReadOnlyList<AdminLiveStatsRawSnapshot> snapshots,
        bool includeByInstance = false)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        Dictionary<string, MutableEndpoint> map = new(StringComparer.Ordinal);
        foreach (AdminLiveStatsRawSnapshot snap in snapshots)
        {
            IEnumerable<AdminEndpointCountersDto> eps = snap.Endpoints.Count > 0
                ? snap.Endpoints
                : snap.Domains.SelectMany(d => d.Endpoints).Concat(snap.UnassignedEndpoints);

            foreach (AdminEndpointCountersDto ep in eps)
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

    /// <summary>Merges unassigned endpoints by route key from raw v2 snapshots.</summary>
    public static IReadOnlyList<AdminEndpointStatsDto> MergeUnassignedEndpoints(
        IReadOnlyList<AdminLiveStatsRawSnapshot> snapshots,
        bool includeByInstance = false)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        Dictionary<string, MutableEndpoint> map = new(StringComparer.Ordinal);
        foreach (AdminLiveStatsRawSnapshot snap in snapshots)
        {
            foreach (AdminEndpointCountersDto ep in snap.UnassignedEndpoints)
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

    /// <summary>Projects one raw domain row to fat + impact (single instance).</summary>
    public static AdminDomainStatsDto ToDomainStats(AdminDomainCountersDto raw, string? instanceId = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) =
            AdminStatsMath.BuildAll(
                raw.OcHits, raw.OcMisses, raw.OcBypass,
                raw.FcHits, raw.FcMisses, raw.FcStale, raw.FcBypass,
                raw.FactoryRuns, raw.FactoryFailures);

        return new AdminDomainStatsDto
        {
            Name = raw.Name,
            InstanceId = raw.InstanceId ?? instanceId,
            Version = raw.Version,
            VersionIsRuntimeOverride = raw.VersionIsRuntimeOverride,
            SchedulePhase = raw.SchedulePhase,
            LastInvalidationUtc = raw.LastInvalidationUtc,
            Invalidations = raw.Invalidations,
            Requests = requests,
            Oc = oc,
            Fc = fc,
            Pipeline = pipeline,
            Impact = BuildImpact(
                requests,
                raw.FactoryRuns,
                raw.FactoryDurationSumMs,
                raw.FactoryDurationCount,
                raw.FactoryResultSizeSumBytes,
                raw.FactoryResultSizeCount),
            Endpoints = raw.Endpoints.Select(e => ToEndpointStats(e, instanceId)).ToArray()
        };
    }

    /// <summary>Projects one raw endpoint row to fat + impact.</summary>
    public static AdminEndpointStatsDto ToEndpointStats(AdminEndpointCountersDto raw, string? instanceId = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) =
            AdminStatsMath.BuildAll(
                raw.OcHits, raw.OcMisses, raw.OcBypass,
                raw.FcHits, raw.FcMisses, raw.FcStale, raw.FcBypass,
                raw.FactoryRuns, raw.FactoryFailures);

        return new AdminEndpointStatsDto
        {
            Route = raw.Route,
            InstanceId = raw.InstanceId ?? instanceId,
            ConfiguredDomain = raw.ConfiguredDomain,
            Requests = requests,
            Oc = oc,
            Fc = fc,
            Pipeline = pipeline,
            Impact = BuildImpact(
                requests,
                raw.FactoryRuns,
                raw.FactoryDurationSumMs,
                raw.FactoryDurationCount,
                raw.FactoryResultSizeSumBytes,
                raw.FactoryResultSizeCount)
        };
    }

    private static CacheImpactKpiDto BuildImpact(
        long requests,
        long factoryRuns,
        double? durationSumMs,
        long durationCount,
        long? sizeSumBytes = null,
        long sizeCount = 0)
    {
        CacheImpactKpiDto impact = ImpactMath.Compute(
            requests, factoryRuns, durationSumMs, durationCount, sizeSumBytes, sizeCount);
        return new CacheImpactKpiDto
        {
            FactoryAvoidance = impact.FactoryAvoidance,
            FactoryShare = impact.FactoryShare,
            AvgFactoryDurationMs = impact.AvgFactoryDurationMs,
            EstFactoryTimeSavedMs = impact.EstFactoryTimeSavedMs,
            TimeSavedRatio = impact.TimeSavedRatio,
            FactoryDurationSumMs = durationSumMs,
            FactoryDurationCount = durationCount,
            AvgFactoryResultSizeBytes = impact.AvgFactoryResultSizeBytes,
            EstPayloadOffloadBytes = impact.EstPayloadOffloadBytes,
            FactoryResultSizeSumBytes = sizeSumBytes,
            FactoryResultSizeCount = sizeCount,
            Benefit = impact.Benefit,
            Candidate = impact.Candidate,
            LowRequestSample = impact.LowRequestSample,
            LowDurationSample = impact.LowDurationSample,
            LowSizeSample = impact.LowSizeSample
        };
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
        private double _durationSumMs;
        private long _durationCount;
        private long _sizeSumBytes;
        private long _sizeCount;
        private readonly Dictionary<string, MutableEndpoint> _endpoints = new(StringComparer.Ordinal);
        private readonly List<AdminDomainStatsDto> _instanceRows = [];

        public void Add(AdminDomainCountersDto d, string instanceId)
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

            _ocHits += d.OcHits;
            _ocMisses += d.OcMisses;
            _ocBypass += d.OcBypass;
            _fcHits += d.FcHits;
            _fcMisses += d.FcMisses;
            _fcStale += d.FcStale;
            _fcBypass += d.FcBypass;
            _fcRuns += d.FactoryRuns;
            _fcFails += d.FactoryFailures;
            if (d.FactoryDurationCount > 0 && d.FactoryDurationSumMs is double ms)
            {
                _durationSumMs += ms;
                _durationCount += d.FactoryDurationCount;
            }

            if (d.FactoryResultSizeCount > 0 && d.FactoryResultSizeSumBytes is long sz)
            {
                _sizeSumBytes += sz;
                _sizeCount += d.FactoryResultSizeCount;
            }

            _instanceRows.Add(ToDomainStats(d, d.InstanceId ?? instanceId));

            foreach (AdminEndpointCountersDto ep in d.Endpoints)
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

            double? durationSum = _durationCount > 0 ? _durationSumMs : null;
            long? sizeSum = _sizeCount > 0 ? _sizeSumBytes : null;

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
                InstanceSpread = spread,
                Impact = BuildImpact(requests, _fcRuns, durationSum, _durationCount, sizeSum, _sizeCount)
            };
        }
    }

    private sealed class MutableEndpoint(string route, string? configuredDomain)
    {
        public string Route { get; } = route;
        public string? ConfiguredDomain { get; private set; } = configuredDomain;

        private long _ocHits, _ocMisses, _ocBypass;
        private long _fcHits, _fcMisses, _fcStale, _fcBypass, _fcRuns, _fcFails;
        private double _durationSumMs;
        private long _durationCount;
        private long _sizeSumBytes;
        private long _sizeCount;
        private readonly List<AdminEndpointStatsDto> _instanceRows = [];

        public void Add(AdminEndpointCountersDto ep, string instanceId)
        {
            ConfiguredDomain ??= ep.ConfiguredDomain;
            _ocHits += ep.OcHits;
            _ocMisses += ep.OcMisses;
            _ocBypass += ep.OcBypass;
            _fcHits += ep.FcHits;
            _fcMisses += ep.FcMisses;
            _fcStale += ep.FcStale;
            _fcBypass += ep.FcBypass;
            _fcRuns += ep.FactoryRuns;
            _fcFails += ep.FactoryFailures;
            if (ep.FactoryDurationCount > 0 && ep.FactoryDurationSumMs is double ms)
            {
                _durationSumMs += ms;
                _durationCount += ep.FactoryDurationCount;
            }

            if (ep.FactoryResultSizeCount > 0 && ep.FactoryResultSizeSumBytes is long sz)
            {
                _sizeSumBytes += sz;
                _sizeCount += ep.FactoryResultSizeCount;
            }

            _instanceRows.Add(ToEndpointStats(ep, ep.InstanceId ?? instanceId));
        }

        public AdminEndpointStatsDto ToDto(bool includeByInstance)
        {
            (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) =
                AdminStatsMath.BuildAll(
                    _ocHits, _ocMisses, _ocBypass,
                    _fcHits, _fcMisses, _fcStale, _fcBypass,
                    _fcRuns, _fcFails);

            double? durationSum = _durationCount > 0 ? _durationSumMs : null;
            long? sizeSum = _sizeCount > 0 ? _sizeSumBytes : null;

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
                InstanceSpread = spread,
                Impact = BuildImpact(requests, _fcRuns, durationSum, _durationCount, sizeSum, _sizeCount)
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
