namespace CacheOrchestrator.Admin;

/// <summary>
/// Projects canonical raw counters into the legacy (v1) fat Admin stats DTOs.
/// Used by <c>GET …/stats</c> until removed in 3.0.
/// </summary>
public static class AdminStatsV1Mapper
{
    /// <summary>Maps raw domain counters to a fat domain stats row (without nested endpoints).</summary>
    public static AdminDomainStatsDto ToDomainStats(
        AdminDomainCountersDto raw,
        IReadOnlyList<AdminEndpointStatsDto>? endpoints = null)
    {
        ArgumentNullException.ThrowIfNull(raw);

        (long requests, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipeline) =
            AdminStatsMath.BuildAll(
                raw.OutputCacheHits, raw.OutputCacheMisses, raw.OutputCacheBypass,
                raw.DataCacheHits, raw.DataCacheMisses, raw.DataCacheStale, raw.DataCacheBypass,
                raw.FactoryRuns, raw.FactoryFailures,
                raw.OutputCacheOff);

        return new AdminDomainStatsDto
        {
            Name = raw.Name,
            InstanceId = raw.InstanceId,
            Version = raw.Version,
            VersionIsRuntimeOverride = raw.VersionIsRuntimeOverride,
            SchedulePhase = raw.SchedulePhase,
            LastInvalidationUtc = raw.LastInvalidationUtc,
            Invalidations = raw.Invalidations,
            Requests = requests,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipeline,
            Endpoints = endpoints ?? []
        };
    }

    /// <summary>Maps raw endpoint counters to a fat endpoint stats row.</summary>
    public static AdminEndpointStatsDto ToEndpointStats(AdminEndpointCountersDto raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        (long requests, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipeline) =
            AdminStatsMath.BuildAll(
                raw.OutputCacheHits, raw.OutputCacheMisses, raw.OutputCacheBypass,
                raw.DataCacheHits, raw.DataCacheMisses, raw.DataCacheStale, raw.DataCacheBypass,
                raw.FactoryRuns, raw.FactoryFailures,
                raw.OutputCacheOff);

        return new AdminEndpointStatsDto
        {
            Route = raw.Route,
            InstanceId = raw.InstanceId,
            ConfiguredDomain = raw.ConfiguredDomain,
            Requests = requests,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipeline
        };
    }

    /// <summary>Maps a full raw snapshot to the legacy fat snapshot (endpoints not nested under domains).</summary>
    public static AdminLiveStatsSnapshot ToLiveSnapshot(AdminLiveStatsRawSnapshot raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        List<AdminEndpointStatsDto> allEndpoints = [.. raw.Endpoints.Select(ToEndpointStats)];
        List<AdminEndpointStatsDto> unassigned = [.. raw.UnassignedEndpoints.Select(ToEndpointStats)];

        Dictionary<string, List<AdminEndpointStatsDto>> byDomain = new(StringComparer.Ordinal);
        foreach (AdminEndpointStatsDto ep in allEndpoints)
        {
            if (string.IsNullOrEmpty(ep.ConfiguredDomain))
                continue;
            if (!byDomain.TryGetValue(ep.ConfiguredDomain, out List<AdminEndpointStatsDto>? list))
            {
                list = [];
                byDomain[ep.ConfiguredDomain] = list;
            }

            list.Add(ep);
        }

        List<AdminDomainStatsDto> domains = [];
        foreach (AdminDomainCountersDto d in raw.Domains)
        {
            byDomain.TryGetValue(d.Name, out List<AdminEndpointStatsDto>? eps);
            domains.Add(ToDomainStats(d, eps ?? []));
        }

        return new AdminLiveStatsSnapshot
        {
            InstanceId = raw.InstanceId,
            CollectedAtUtc = raw.CollectedAtUtc,
            Domains = domains,
            UnassignedEndpoints = unassigned,
            Endpoints = allEndpoints
        };
    }

    /// <summary>Builds fat layer DTOs from a counter snapshot (collector / tests).</summary>
    internal static (
        long Requests,
        AdminLayerDto OutputCache,
        AdminDataCacheLayerDto DataCache,
        AdminPipelineDto Pipeline) ToLayerStats(in AdminCounterSnapshot c) =>
        AdminStatsMath.BuildAll(
            c.OutputCacheHits, c.OutputCacheMisses, c.OutputCacheBypass,
            c.DataCacheHits, c.DataCacheMisses, c.DataCacheStale, c.DataCacheBypass,
            c.FactoryRuns, c.FactoryFailures,
            c.OutputCacheOff);
}
