using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Assembles Local Admin read models (stats, domain config) from live counters + options.
/// Canonical path is raw (v2); fat (v1) is projected via <see cref="AdminStatsV1Mapper"/>.
/// </summary>
internal sealed class AdminQueryService
{
    private readonly IAdminStatsCollector _stats;
    private readonly IAdminEndpointCatalog _endpoints;
    private readonly IDomainCacheOptionsProvider _domainOptions;
    private readonly IDomainRuntimeOverrideStore _overrides;
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options;
    private readonly TimeProvider _time;

    public AdminQueryService(
        IAdminStatsCollector stats,
        IAdminEndpointCatalog endpoints,
        IDomainCacheOptionsProvider domainOptions,
        IDomainRuntimeOverrideStore overrides,
        IOptionsMonitor<CacheOrchestratorOptions> options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(domainOptions);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(options);

        _stats = stats;
        _endpoints = endpoints;
        _domainOptions = domainOptions;
        _overrides = overrides;
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
    }

    public AdminHealthDto GetHealth()
    {
        CacheOrchestratorOptions.AdminOptions admin = _options.CurrentValue.Admin;
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset started = AdminProcessInfo.StartedAtUtc;
        long uptimeSeconds = (long)Math.Max(0, (now - started).TotalSeconds);
        long requests = 0;
        try
        {
            AdminLiveStatsRawSnapshot snap = _stats.GetRawSnapshot();
            foreach (AdminDomainCountersDto d in snap.Domains)
                requests += RequestDenominator(d);
            if (requests == 0)
            {
                foreach (AdminEndpointCountersDto e in snap.UnassignedEndpoints)
                    requests += RequestDenominator(e);
            }
        }
        catch
        {
            // Health must not fail if counters are unavailable.
        }

        return new AdminHealthDto
        {
            Healthy = true,
            InstanceId = AdminInstanceId.Resolve(_options.CurrentValue),
            UtcNow = now,
            AdminEnabled = admin.Enabled,
            StartedAtUtc = started,
            UptimeSeconds = uptimeSeconds,
            Requests = requests
        };
    }

    /// <summary>Canonical raw stats (Admin API <c>GET …/stats/v2</c>).</summary>
    public AdminLiveStatsRawSnapshot GetStatsRaw()
    {
        string instanceId = AdminInstanceId.Resolve(_options.CurrentValue);
        AdminLiveStatsRawSnapshot raw = _stats.GetRawSnapshot();
        IReadOnlyList<AdminEndpointInfoDto> discovered = _endpoints.GetEndpoints();

        Dictionary<string, AdminEndpointInfoDto> discoveredByRoute =
            discovered.ToDictionary(e => e.Route, StringComparer.Ordinal);

        Dictionary<string, List<AdminEndpointCountersDto>> byDomain = new(StringComparer.Ordinal);
        List<AdminEndpointCountersDto> unassigned = [];
        List<AdminEndpointCountersDto> allEndpoints = [];

        foreach (AdminEndpointCountersDto ep in raw.UnassignedEndpoints)
        {
            string? domain = null;
            if (discoveredByRoute.TryGetValue(ep.Route, out AdminEndpointInfoDto? info))
                domain = info.ConfiguredDomain;
            domain ??= ep.ConfiguredDomain;

            AdminEndpointCountersDto normalized = CloneEndpoint(ep, instanceId, domain);
            allEndpoints.Add(normalized);

            if (string.IsNullOrEmpty(domain))
            {
                unassigned.Add(normalized);
                continue;
            }

            if (!byDomain.TryGetValue(domain, out List<AdminEndpointCountersDto>? list))
            {
                list = [];
                byDomain[domain] = list;
            }

            list.Add(normalized);
        }

        HashSet<string> domainNames = new(StringComparer.Ordinal);
        foreach (AdminDomainCountersDto d in raw.Domains)
            domainNames.Add(d.Name);
        foreach (string d in _options.CurrentValue.Domains.Keys)
            domainNames.Add(DomainName.Normalize(d));
        foreach (string d in _overrides.GetOverriddenDomains())
            domainNames.Add(d);
        foreach (AdminEndpointInfoDto ep in discovered)
        {
            if (!string.IsNullOrEmpty(ep.ConfiguredDomain))
                domainNames.Add(ep.ConfiguredDomain);
        }

        Dictionary<string, AdminDomainCountersDto> rawDomains =
            raw.Domains.ToDictionary(d => d.Name, StringComparer.Ordinal);

        List<AdminDomainCountersDto> domains = [];
        foreach (string name in domainNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            DomainCacheOptions opts = _domainOptions.GetOrCreateDomainOptions(name);
            DomainRuntimeOverride? ov = _overrides.Get(name);
            rawDomains.TryGetValue(name, out AdminDomainCountersDto? counters);

            if (!byDomain.TryGetValue(name, out List<AdminEndpointCountersDto>? epList))
                epList = [];

            HashSet<string> present = new(epList.Select(e => e.Route), StringComparer.Ordinal);
            foreach (AdminEndpointInfoDto info in discovered)
            {
                if (!string.Equals(info.ConfiguredDomain, name, StringComparison.Ordinal))
                    continue;
                if (!present.Add(info.Route))
                    continue;

                AdminEndpointCountersDto empty = EmptyEndpoint(info.Route, name, instanceId);
                epList.Add(empty);
                allEndpoints.Add(empty);
            }

            epList.Sort(static (a, b) => string.CompareOrdinal(a.Route, b.Route));

            AdminDomainCountersDto row = BuildDomainRow(
                name,
                instanceId,
                opts,
                ov,
                counters,
                epList);

            domains.Add(row);
        }

        allEndpoints.Sort(static (a, b) => string.CompareOrdinal(a.Route, b.Route));

        return new AdminLiveStatsRawSnapshot
        {
            InstanceId = instanceId,
            CollectedAtUtc = _time.GetUtcNow(),
            Domains = domains,
            UnassignedEndpoints = [.. unassigned.OrderBy(e => e.Route, StringComparer.Ordinal)],
            Endpoints = allEndpoints
        };
    }

    /// <summary>
    /// Legacy fat stats (Admin API <c>GET …/stats</c>). Projects from <see cref="GetStatsRaw"/>.
    /// </summary>
    public AdminLiveStatsSnapshot GetStats() =>
        AdminStatsV1Mapper.ToLiveSnapshot(GetStatsRaw());

    public IReadOnlyList<AdminEndpointInfoDto> GetEndpoints() => _endpoints.GetEndpoints();

    public IReadOnlyList<AdminDomainConfigDto> GetDomains()
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (string d in _options.CurrentValue.Domains.Keys)
            names.Add(DomainName.Normalize(d));
        foreach (string d in _overrides.GetOverriddenDomains())
            names.Add(d);
        foreach (AdminEndpointInfoDto ep in _endpoints.GetEndpoints())
        {
            if (!string.IsNullOrEmpty(ep.ConfiguredDomain))
                names.Add(ep.ConfiguredDomain);
        }

        foreach (AdminDomainCountersDto d in _stats.GetRawSnapshot().Domains)
            names.Add(d.Name);

        return [.. names.OrderBy(n => n, StringComparer.Ordinal).Select(GetDomainConfig)];
    }

    public AdminDomainConfigDto? GetDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;
        return GetDomainConfig(DomainName.Normalize(domain));
    }

    public AdminDomainConfigDto GetDomainConfig(string normalizedDomain)
    {
        DomainCacheOptions opts = _domainOptions.GetOrCreateDomainOptions(normalizedDomain);
        DomainRuntimeOverride? ov = _overrides.Get(normalizedDomain);

        return new AdminDomainConfigDto
        {
            Name = normalizedDomain,
            Version = opts.Version,
            VersionIsRuntimeOverride = ov?.Version is not null,
            OutputCacheEnabled = opts.OutputCacheEnabled,
            FusionCacheEnabled = opts.FusionCacheEnabled,
            FusionCacheInstanceName = opts.FusionCacheInstanceName,
            OutputCacheTtlSeconds = (int)opts.OutputTtl.TotalSeconds,
            FusionCacheSoftTtlSeconds = (int)opts.FusionCacheSoftTtl.TotalSeconds,
            FusionCacheHardTtlSeconds = (int)opts.FusionCacheHardTtl.TotalSeconds,
            FusionCacheFailSafeSeconds = (int)opts.FusionCacheFailSafe.TotalSeconds,
            ClientTtlSeconds = opts.ClientTtlSeconds,
            ClientTtlMinSeconds = opts.ClientTtlMinSeconds,
            ScheduledUpdateUtc = opts.ScheduledUpdateUtc,
            SchedulePhase = ResolveSchedulePhase(opts),
            RuntimeOverrides = ov is null
                ? null
                : new AdminRuntimeOverrideFlagsDto
                {
                    Version = ov.Version is not null,
                    OutputCacheTtl = ov.OutputCacheTtlSeconds is not null,
                    FusionCacheSoftTtl = ov.FusionCacheSoftTtlSeconds is not null,
                    FusionCacheHardTtl = ov.FusionCacheHardTtlSeconds is not null,
                    FusionCacheFailSafe = ov.FusionCacheFailSafeSeconds is not null,
                    ClientTtl = ov.ClientTtlSeconds is not null,
                    ClientTtlMin = ov.ClientTtlMinSeconds is not null
                }
        };
    }

    private AdminDomainCountersDto BuildDomainRow(
        string name,
        string instanceId,
        DomainCacheOptions opts,
        DomainRuntimeOverride? ov,
        AdminDomainCountersDto? counters,
        List<AdminEndpointCountersDto> epList)
    {
        long ocH = counters?.OcHits ?? 0;
        long ocM = counters?.OcMisses ?? 0;
        long ocB = counters?.OcBypass ?? 0;
        long fcH = counters?.FcHits ?? 0;
        long fcM = counters?.FcMisses ?? 0;
        long fcS = counters?.FcStale ?? 0;
        long fcB = counters?.FcBypass ?? 0;
        long runs = counters?.FactoryRuns ?? 0;
        long fails = counters?.FactoryFailures ?? 0;
        double? durationSum = counters?.FactoryDurationSumMs;
        long durationCount = counters?.FactoryDurationCount ?? 0;
        long? sizeSum = counters?.FactoryResultSizeSumBytes;
        long sizeCount = counters?.FactoryResultSizeCount ?? 0;

        long domainRequests = AdminStatsMath.Requests(ocH, ocM, ocB, fcH, fcM, fcS, fcB);

        // If domain counters empty but endpoints have traffic, rebuild from endpoint sums.
        if (domainRequests == 0 && epList.Count > 0)
        {
            ocH = ocM = ocB = fcH = fcM = fcS = fcB = runs = fails = 0;
            durationSum = null;
            durationCount = 0;
            sizeSum = null;
            sizeCount = 0;
            double sumMs = 0;
            long sumCount = 0;
            long sumSize = 0;
            long sumSizeCount = 0;
            foreach (AdminEndpointCountersDto e in epList)
            {
                ocH += e.OcHits;
                ocM += e.OcMisses;
                ocB += e.OcBypass;
                fcH += e.FcHits;
                fcM += e.FcMisses;
                fcS += e.FcStale;
                fcB += e.FcBypass;
                runs += e.FactoryRuns;
                fails += e.FactoryFailures;
                if (e.FactoryDurationCount > 0 && e.FactoryDurationSumMs is double ms)
                {
                    sumMs += ms;
                    sumCount += e.FactoryDurationCount;
                }

                if (e.FactoryResultSizeCount > 0 && e.FactoryResultSizeSumBytes is long sz)
                {
                    sumSize += sz;
                    sumSizeCount += e.FactoryResultSizeCount;
                }
            }

            if (sumCount > 0)
            {
                durationSum = sumMs;
                durationCount = sumCount;
            }

            if (sumSizeCount > 0)
            {
                sizeSum = sumSize;
                sizeCount = sumSizeCount;
            }
        }

        return new AdminDomainCountersDto
        {
            Name = name,
            InstanceId = instanceId,
            Version = opts.Version,
            VersionIsRuntimeOverride = ov?.Version is not null,
            SchedulePhase = ResolveSchedulePhase(opts),
            LastInvalidationUtc = counters?.LastInvalidationUtc,
            Invalidations = counters?.Invalidations ?? 0,
            OcHits = ocH,
            OcMisses = ocM,
            OcBypass = ocB,
            FcHits = fcH,
            FcMisses = fcM,
            FcStale = fcS,
            FcBypass = fcB,
            FactoryRuns = runs,
            FactoryFailures = fails,
            FactoryDurationSumMs = durationSum,
            FactoryDurationCount = durationCount,
            FactoryResultSizeSumBytes = sizeSum,
            FactoryResultSizeCount = sizeCount,
            Endpoints = epList
        };
    }

    private string? ResolveSchedulePhase(DomainCacheOptions opts)
    {
        if (opts.ScheduledUpdateUtc is null)
            return null;

        ClientCacheHeaderGenerator.Result built = ClientCacheHeaderGenerator.Build(opts, _time.GetUtcNow());
        string phase = XCacheHeaderFormatter.PhaseToString(built.Phase);
        return phase == "n/a" ? null : phase;
    }

    private static AdminEndpointCountersDto CloneEndpoint(
        AdminEndpointCountersDto ep,
        string instanceId,
        string? domain) =>
        new()
        {
            Route = ep.Route,
            InstanceId = instanceId,
            ConfiguredDomain = domain,
            OcHits = ep.OcHits,
            OcMisses = ep.OcMisses,
            OcBypass = ep.OcBypass,
            FcHits = ep.FcHits,
            FcMisses = ep.FcMisses,
            FcStale = ep.FcStale,
            FcBypass = ep.FcBypass,
            FactoryRuns = ep.FactoryRuns,
            FactoryFailures = ep.FactoryFailures,
            FactoryDurationSumMs = ep.FactoryDurationSumMs,
            FactoryDurationCount = ep.FactoryDurationCount,
            FactoryResultSizeSumBytes = ep.FactoryResultSizeSumBytes,
            FactoryResultSizeCount = ep.FactoryResultSizeCount
        };

    private static AdminEndpointCountersDto EmptyEndpoint(string route, string domain, string instanceId) =>
        new()
        {
            Route = route,
            InstanceId = instanceId,
            ConfiguredDomain = domain
        };

    private static long RequestDenominator(AdminDomainCountersDto d) =>
        AdminStatsMath.Requests(
            d.OcHits, d.OcMisses, d.OcBypass,
            d.FcHits, d.FcMisses, d.FcStale, d.FcBypass);

    private static long RequestDenominator(AdminEndpointCountersDto e) =>
        AdminStatsMath.Requests(
            e.OcHits, e.OcMisses, e.OcBypass,
            e.FcHits, e.FcMisses, e.FcStale, e.FcBypass);
}
