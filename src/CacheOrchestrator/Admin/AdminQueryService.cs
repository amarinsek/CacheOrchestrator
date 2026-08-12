using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Assembles Local Admin read models (stats, domain config) from live counters + options.
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
        return new AdminHealthDto
        {
            Healthy = true,
            InstanceId = AdminInstanceId.Resolve(admin),
            UtcNow = _time.GetUtcNow(),
            AdminEnabled = admin.Enabled
        };
    }

    public AdminLiveStatsSnapshot GetStats()
    {
        string instanceId = AdminInstanceId.Resolve(_options.CurrentValue.Admin);
        AdminLiveStatsSnapshot raw = _stats.GetSnapshot();
        IReadOnlyList<AdminEndpointInfoDto> discovered = _endpoints.GetEndpoints();

        Dictionary<string, AdminEndpointInfoDto> discoveredByRoute =
            discovered.ToDictionary(e => e.Route, StringComparer.Ordinal);

        Dictionary<string, List<AdminEndpointStatsDto>> byDomain = new(StringComparer.Ordinal);
        List<AdminEndpointStatsDto> unassigned = [];
        List<AdminEndpointStatsDto> allEndpoints = [];

        foreach (AdminEndpointStatsDto ep in raw.UnassignedEndpoints)
        {
            string? domain = null;
            if (discoveredByRoute.TryGetValue(ep.Route, out AdminEndpointInfoDto? info))
                domain = info.ConfiguredDomain;
            domain ??= ep.ConfiguredDomain;

            AdminEndpointStatsDto normalized = CloneEndpoint(ep, instanceId, domain);
            allEndpoints.Add(normalized);

            if (string.IsNullOrEmpty(domain))
            {
                unassigned.Add(normalized);
                continue;
            }

            if (!byDomain.TryGetValue(domain, out List<AdminEndpointStatsDto>? list))
            {
                list = [];
                byDomain[domain] = list;
            }

            list.Add(normalized);
        }

        HashSet<string> domainNames = new(StringComparer.Ordinal);
        foreach (AdminDomainStatsDto d in raw.Domains)
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

        Dictionary<string, AdminDomainStatsDto> rawDomains =
            raw.Domains.ToDictionary(d => d.Name, StringComparer.Ordinal);

        List<AdminDomainStatsDto> domains = [];
        foreach (string name in domainNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            DomainCacheOptions opts = _domainOptions.GetOrCreateDomainOptions(name);
            DomainRuntimeOverride? ov = _overrides.Get(name);
            rawDomains.TryGetValue(name, out AdminDomainStatsDto? counters);

            if (!byDomain.TryGetValue(name, out List<AdminEndpointStatsDto>? epList))
                epList = [];

            HashSet<string> present = new(epList.Select(e => e.Route), StringComparer.Ordinal);
            foreach (AdminEndpointInfoDto info in discovered)
            {
                if (!string.Equals(info.ConfiguredDomain, name, StringComparison.Ordinal))
                    continue;
                if (!present.Add(info.Route))
                    continue;

                AdminEndpointStatsDto empty = EmptyEndpoint(info.Route, name, instanceId);
                epList.Add(empty);
                allEndpoints.Add(empty);
            }

            epList.Sort(static (a, b) => string.CompareOrdinal(a.Route, b.Route));

            (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) =
                counters is null
                    ? EmptyStats()
                    : (
                        counters.Requests,
                        counters.Oc,
                        counters.Fc,
                        counters.Pipeline);

            // If domain counters empty but endpoints have traffic, rebuild from endpoint sums.
            if (requests == 0 && epList.Count > 0)
            {
                long ocH = 0, ocM = 0, ocB = 0, fcH = 0, fcM = 0, fcS = 0, fcB = 0, runs = 0, fails = 0;
                foreach (AdminEndpointStatsDto e in epList)
                {
                    ocH += e.Oc.Hits;
                    ocM += e.Oc.Misses;
                    ocB += e.Oc.Bypass;
                    fcH += e.Fc.Hits;
                    fcM += e.Fc.Misses;
                    fcS += e.Fc.Stale;
                    fcB += e.Fc.Bypass;
                    runs += e.Fc.FactoryRuns;
                    fails += e.Fc.FactoryFailures;
                }

                (requests, oc, fc, pipeline) = AdminStatsMath.BuildAll(
                    ocH, ocM, ocB, fcH, fcM, fcS, fcB, runs, fails);
            }

            domains.Add(new AdminDomainStatsDto
            {
                Name = name,
                InstanceId = instanceId,
                Version = opts.Version,
                VersionIsRuntimeOverride = ov?.Version is not null,
                SchedulePhase = ResolveSchedulePhase(opts),
                LastInvalidationUtc = counters?.LastInvalidationUtc,
                Invalidations = counters?.Invalidations ?? 0,
                Requests = requests,
                Oc = oc,
                Fc = fc,
                Pipeline = pipeline,
                Endpoints = epList
            });
        }

        allEndpoints.Sort(static (a, b) => string.CompareOrdinal(a.Route, b.Route));

        return new AdminLiveStatsSnapshot
        {
            InstanceId = instanceId,
            CollectedAtUtc = _time.GetUtcNow(),
            Domains = domains,
            UnassignedEndpoints = [.. unassigned.OrderBy(e => e.Route, StringComparer.Ordinal)],
            Endpoints = allEndpoints
        };
    }

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

        foreach (AdminDomainStatsDto d in _stats.GetSnapshot().Domains)
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

    private string? ResolveSchedulePhase(DomainCacheOptions opts)
    {
        if (opts.ScheduledUpdateUtc is null)
            return null;

        ClientCacheHeaderGenerator.Result built = ClientCacheHeaderGenerator.Build(opts, _time.GetUtcNow());
        string phase = XCacheHeaderFormatter.PhaseToString(built.Phase);
        return phase == "n/a" ? null : phase;
    }

    private static AdminEndpointStatsDto CloneEndpoint(
        AdminEndpointStatsDto ep,
        string instanceId,
        string? domain) =>
        new()
        {
            Route = ep.Route,
            InstanceId = instanceId,
            ConfiguredDomain = domain,
            Requests = ep.Requests,
            Oc = ep.Oc,
            Fc = ep.Fc,
            Pipeline = ep.Pipeline
        };

    private static AdminEndpointStatsDto EmptyEndpoint(string route, string domain, string instanceId)
    {
        (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) = EmptyStats();
        return new AdminEndpointStatsDto
        {
            Route = route,
            InstanceId = instanceId,
            ConfiguredDomain = domain,
            Requests = requests,
            Oc = oc,
            Fc = fc,
            Pipeline = pipeline
        };
    }

    private static (long, AdminLayerDto, AdminFusionLayerDto, AdminPipelineDto) EmptyStats() =>
        AdminStatsMath.BuildAll(0, 0, 0, 0, 0, 0, 0, 0, 0);
}
