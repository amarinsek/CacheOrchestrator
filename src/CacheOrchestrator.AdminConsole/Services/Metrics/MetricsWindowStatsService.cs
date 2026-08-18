using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Services.Hints;

namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>
/// Builds domain/endpoint stats from Prometheus for a selected time window
/// so the Console can show windowed traffic without Local Admin process totals.
/// </summary>
public sealed class MetricsWindowStatsService
{
    private readonly IMetricsQueryClient _client;
    private readonly MetricsQueryService _status;
    private readonly HintEngine _hints;
    private readonly AdminFanOutService _fanOut;
    private readonly TimeProvider _time;

    public MetricsWindowStatsService(
        IMetricsQueryClient client,
        MetricsQueryService status,
        HintEngine hints,
        AdminFanOutService fanOut,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(hints);
        ArgumentNullException.ThrowIfNull(fanOut);
        ArgumentNullException.ThrowIfNull(time);
        _client = client;
        _status = status;
        _hints = hints;
        _fanOut = fanOut;
        _time = time;
    }

    /// <summary>
    /// Loads window aggregates. Never throws for “not configured”; returns status envelope.
    /// Not cached: tables must stay aligned with fresh Prom samples (charts use query_range separately).
    /// </summary>
    public async Task<WindowStatsDto> GetAsync(
        string? range,
        string? from,
        string? to,
        string? domainsCsv = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        MetricsWindow window = MetricsWindow.Resolve(range, from, to, now);
        string statsWindow = window.IsAbsolute
            ? $"Metrics store · {window.Start:u} → {window.End:u}"
            : $"Metrics store · last {window.RangeLabel}";

        MetricsStatusDto probe = await _status.GetStatusAsync(probe: true, cancellationToken).ConfigureAwait(false);
        if (probe.Status != MetricsStoreStatusCodes.Connected)
        {
            return Empty(probe.Status, window, now, statsWindow, probe.Error);
        }

        try
        {
            string rw = window.PromRangeDuration;
            IReadOnlyList<string> domainFilter = ParseCsv(domainsCsv);

            // Window counts: last_over_time − offset (see WindowCountBy). Do not use bare increase():
            // it under-counts the first sample and can yield 0 that blocks PromQL `or`, so a brand-new
            // series appears on the first refresh then vanishes on the second.
            Task<IReadOnlyList<PrometheusInstantSample>> ocTask = QueryAsync(
                WindowCountBy("domain,result", MetricsPanelCatalog.OcRequests, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> fcTask = QueryAsync(
                WindowCountBy("domain,result", MetricsPanelCatalog.FcRequests, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> invTask = QueryAsync(
                WindowCountBy("domain", MetricsPanelCatalog.Invalidations, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facSumTask = QueryAsync(
                WindowCountBy("domain", MetricsPanelCatalog.FactoryDurationSum, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facCntTask = QueryAsync(
                WindowCountBy("domain", MetricsPanelCatalog.FactoryDurationCount, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> ocRouteTask = QueryAsync(
                WindowCountBy("route,result,domain", MetricsPanelCatalog.OcRequests, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> fcRouteTask = QueryAsync(
                WindowCountBy("route,result,domain", MetricsPanelCatalog.FcRequests, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facSumRouteTask = QueryAsync(
                WindowCountBy("route,domain", MetricsPanelCatalog.FactoryDurationSum, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facCntRouteTask = QueryAsync(
                WindowCountBy("route,domain", MetricsPanelCatalog.FactoryDurationCount, rw, domainFilter),
                window.End, cancellationToken);
            // Per-instance (scrape label instance_id → missing becomes "undefined")
            Task<IReadOnlyList<PrometheusInstantSample>> ocInstTask = QueryAsync(
                WindowCountBy("domain,result,instance_id", MetricsPanelCatalog.OcRequests, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> fcInstTask = QueryAsync(
                WindowCountBy("domain,result,instance_id", MetricsPanelCatalog.FcRequests, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> invInstTask = QueryAsync(
                WindowCountBy("domain,instance_id", MetricsPanelCatalog.Invalidations, rw, domainFilter),
                window.End, cancellationToken);
            // Endpoint × instance (for instance detail endpoint list)
            Task<IReadOnlyList<PrometheusInstantSample>> ocRouteInstTask = QueryAsync(
                WindowCountBy("route,result,instance_id", MetricsPanelCatalog.OcRequests, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> fcRouteInstTask = QueryAsync(
                WindowCountBy("route,result,instance_id", MetricsPanelCatalog.FcRequests, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facSumRouteInstTask = QueryAsync(
                WindowCountBy("route,instance_id", MetricsPanelCatalog.FactoryDurationSum, rw, domainFilter),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facCntRouteInstTask = QueryAsync(
                WindowCountBy("route,instance_id", MetricsPanelCatalog.FactoryDurationCount, rw, domainFilter),
                window.End, cancellationToken);
            // Peak 1m OC rate inside the selected Range (load in-window, not Range average).
            string peakDomQl =
                $"max_over_time(sum by (domain) (rate({MetricsPanelCatalog.OcRequests}{BuildDomainSelector(domainFilter)}[1m]))[{rw}:1m])";
            string peakRouteQl =
                $"max_over_time(sum by (route) (rate({MetricsPanelCatalog.OcRequests}{BuildDomainSelector(domainFilter)}[1m]))[{rw}:1m])";
            Task<IReadOnlyList<PrometheusInstantSample>> peakDomTask =
                QueryAsync(peakDomQl, window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> peakRouteTask =
                QueryAsync(peakRouteQl, window.End, cancellationToken);
            Task<FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>>> cfgTask =
                _fanOut.GetDomainsAsync(cancellationToken);

            await Task.WhenAll(
                    ocTask, fcTask, invTask, facSumTask, facCntTask,
                    ocRouteTask, fcRouteTask, facSumRouteTask, facCntRouteTask,
                    ocInstTask, fcInstTask, invInstTask,
                    ocRouteInstTask, fcRouteInstTask, facSumRouteInstTask, facCntRouteInstTask,
                    peakDomTask, peakRouteTask,
                    cfgTask)
                .ConfigureAwait(false);

            Dictionary<string, LayerBucket> domains = new(StringComparer.OrdinalIgnoreCase);
            AccumulateLayer(domains, await ocTask.ConfigureAwait(false), isOc: true, keyLabel: "domain");
            AccumulateLayer(domains, await fcTask.ConfigureAwait(false), isOc: false, keyLabel: "domain");
            AccumulateInv(domains, await invTask.ConfigureAwait(false));
            AccumulateFactoryDuration(domains, await facSumTask.ConfigureAwait(false), await facCntTask.ConfigureAwait(false), keyLabel: "domain");

            // domain → instanceId → bucket
            Dictionary<string, Dictionary<string, LayerBucket>> domainInst = new(StringComparer.OrdinalIgnoreCase);
            AccumulateLayerByInstance(domainInst, await ocInstTask.ConfigureAwait(false), isOc: true);
            AccumulateLayerByInstance(domainInst, await fcInstTask.ConfigureAwait(false), isOc: false);
            AccumulateInvByInstance(domainInst, await invInstTask.ConfigureAwait(false));

            IReadOnlyList<PrometheusInstantSample> ocRoute = await ocRouteTask.ConfigureAwait(false);
            IReadOnlyList<PrometheusInstantSample> fcRoute = await fcRouteTask.ConfigureAwait(false);
            Dictionary<string, LayerBucket> routes = new(StringComparer.Ordinal);
            AccumulateLayer(routes, ocRoute, isOc: true, keyLabel: "route");
            AccumulateLayer(routes, fcRoute, isOc: false, keyLabel: "route");
            AccumulateFactoryDuration(routes, await facSumRouteTask.ConfigureAwait(false), await facCntRouteTask.ConfigureAwait(false), keyLabel: "route");

            // route → instanceId → bucket
            Dictionary<string, Dictionary<string, LayerBucket>> routeInst = new(StringComparer.Ordinal);
            AccumulateLayerByKeyInstance(routeInst, await ocRouteInstTask.ConfigureAwait(false), isOc: true, keyLabel: "route");
            AccumulateLayerByKeyInstance(routeInst, await fcRouteInstTask.ConfigureAwait(false), isOc: false, keyLabel: "route");
            AccumulateFactoryDurationByKeyInstance(
                routeInst,
                await facSumRouteInstTask.ConfigureAwait(false),
                await facCntRouteInstTask.ConfigureAwait(false),
                keyLabel: "route");

            Dictionary<string, string> routeDomain = new(StringComparer.Ordinal);
            foreach (PrometheusInstantSample s in ocRoute.Concat(fcRoute))
            {
                string route = Label(s.Metric, "route");
                string dom = Label(s.Metric, "domain");
                if (route.Length > 0 && dom.Length > 0 && !routeDomain.ContainsKey(route))
                    routeDomain[route] = dom;
            }

            Dictionary<string, AdminDomainConfigDto> configByName = new(StringComparer.OrdinalIgnoreCase);
            FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>> cfgFan = await cfgTask.ConfigureAwait(false);
            foreach (AdminDomainConfigDto c in cfgFan.Data ?? [])
            {
                configByName.TryAdd(c.Name, c);
            }

            List<AdminDomainStatsDto> domainRows = [];
            foreach ((string name, LayerBucket b) in domains.OrderByDescending(kv => kv.Value.Requests))
            {
                // Refuse path-like "domains" (legacy entity invalidation metric misuse: product-crud/products/42).
                if (string.IsNullOrEmpty(name) || name is "_" || name.Contains('/', StringComparison.Ordinal))
                    continue;

                // Zero-delta / factory-only residual samples stay out of traffic tables.
                if (b.Requests <= 0 && b.Invalidations <= 0)
                    continue;

                List<AdminDomainStatsDto>? byInstance = null;
                AdminInstanceSpreadDto? spread = null;
                if (domainInst.TryGetValue(name, out Dictionary<string, LayerBucket>? instMap) && instMap.Count > 0)
                {
                    byInstance = instMap
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .Where(kv => kv.Value.Requests > 0 || kv.Value.Invalidations > 0)
                        .Select(kv => ToDomain(name, kv.Value, instanceId: kv.Key))
                        .ToList();
                    if (byInstance.Count == 0)
                        byInstance = null;
                    else
                    {
                        spread = new AdminInstanceSpreadDto
                        {
                            OcHitShare = AdminStatsMath.Spread(byInstance.Select(x => x.Oc.HitShare)),
                            FcHitShare = AdminStatsMath.Spread(byInstance.Select(x => x.Fc.HitShare)),
                            FactoryShare = AdminStatsMath.Spread(byInstance.Select(x => x.Fc.FactoryShare)),
                        };
                    }
                }

                configByName.TryGetValue(name, out AdminDomainConfigDto? cfg);
                string version = cfg?.Version ?? "";
                bool verRt = cfg?.VersionIsRuntimeOverride ?? false;

                AdminDomainStatsDto row = ToDomain(name, b, instanceId: null, version, verRt, byInstance, spread);
                domainRows.Add(_hints.WithHints(row, cfg));
            }

            List<AdminEndpointStatsDto> endpointRows = [];
            foreach ((string route, LayerBucket b) in routes.OrderByDescending(kv => kv.Value.Requests))
            {
                if (string.IsNullOrEmpty(route))
                    continue;
                // No traffic in this window → do not list (even if Prom still returns a 0 increase).
                if (b.Requests <= 0)
                    continue;

                routeDomain.TryGetValue(route, out string? dom);

                List<AdminEndpointStatsDto>? byInstance = null;
                AdminInstanceSpreadDto? spread = null;
                if (routeInst.TryGetValue(route, out Dictionary<string, LayerBucket>? instMap) && instMap.Count > 0)
                {
                    byInstance = instMap
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .Where(kv => kv.Value.Requests > 0)
                        .Select(kv => ToEndpoint(route, dom, kv.Value, instanceId: kv.Key))
                        .ToList();
                    if (byInstance.Count == 0)
                        byInstance = null;
                    else
                    {
                        spread = new AdminInstanceSpreadDto
                        {
                            OcHitShare = AdminStatsMath.Spread(byInstance.Select(x => x.Oc.HitShare)),
                            FcHitShare = AdminStatsMath.Spread(byInstance.Select(x => x.Fc.HitShare)),
                            FactoryShare = AdminStatsMath.Spread(byInstance.Select(x => x.Fc.FactoryShare)),
                        };
                    }
                }

                AdminEndpointStatsDto ep = ToEndpoint(route, dom, b, instanceId: null, byInstance, spread);
                endpointRows.Add(_hints.WithHints(ep));
            }

            Dictionary<string, double> peakByDomain = PeakMap(await peakDomTask.ConfigureAwait(false), "domain");
            Dictionary<string, double> peakByRoute = PeakMap(await peakRouteTask.ConfigureAwait(false), "route");

            endpointRows = endpointRows.Select(ep => new AdminEndpointStatsDto
            {
                Route = ep.Route,
                InstanceId = ep.InstanceId,
                ConfiguredDomain = ep.ConfiguredDomain,
                Requests = ep.Requests,
                PeakRequestRate = peakByRoute.TryGetValue(ep.Route, out double pr) ? pr : null,
                Oc = ep.Oc,
                Fc = ep.Fc,
                Pipeline = ep.Pipeline,
                ByInstance = ep.ByInstance,
                InstanceSpread = ep.InstanceSpread,
                Hints = ep.Hints,
                Impact = ep.Impact,
            }).ToList();

            // Nest endpoints under domains for detail convenience
            Dictionary<string, List<AdminEndpointStatsDto>> byDom = new(StringComparer.OrdinalIgnoreCase);
            foreach (AdminEndpointStatsDto ep in endpointRows)
            {
                string d = ep.ConfiguredDomain ?? "";
                if (d.Length == 0) continue;
                if (!byDom.TryGetValue(d, out List<AdminEndpointStatsDto>? list))
                {
                    list = [];
                    byDom[d] = list;
                }

                list.Add(ep);
            }

            domainRows = domainRows.Select(d =>
            {
                IReadOnlyList<AdminEndpointStatsDto> eps = byDom.TryGetValue(d.Name, out List<AdminEndpointStatsDto>? list)
                    ? list
                    : Array.Empty<AdminEndpointStatsDto>();
                return new AdminDomainStatsDto
                {
                    Name = d.Name,
                    InstanceId = d.InstanceId,
                    Version = d.Version,
                    VersionIsRuntimeOverride = d.VersionIsRuntimeOverride,
                    SchedulePhase = d.SchedulePhase,
                    Invalidations = d.Invalidations,
                    Requests = d.Requests,
                    PeakRequestRate = peakByDomain.TryGetValue(d.Name, out double pd) ? pd : null,
                    Oc = d.Oc,
                    Fc = d.Fc,
                    Pipeline = d.Pipeline,
                    Impact = d.Impact,
                    Hints = d.Hints,
                    ByInstance = d.ByInstance,
                    InstanceSpread = d.InstanceSpread,
                    Endpoints = eps,
                };
            }).ToList();

            LayerBucket cluster = domains.Values.Aggregate(new LayerBucket(), (a, b) => a.Add(b));
            var (req, oc, fc, pipe) = cluster.BuildLayers();
            CacheImpactKpiDto impact = ImpactMath.Compute(
                req,
                cluster.FactoryRuns,
                cluster.FactoryDurationSumMs,
                cluster.FactoryDurationCount);
            // Sum per-domain estimates so cluster Time saved matches the domains table
            // (blended cluster avg distorts when domains have different factory costs).
            double domainTimeSavedSum = domainRows
                .Sum(d => d.Impact?.EstFactoryTimeSavedMs ?? 0);
            if (domainRows.Count > 0)
                impact = ImpactMath.WithEstTimeSaved(impact, domainTimeSavedSum);

            IReadOnlyList<AdminHintDto> allHints = HintEngine.CollectFromStats(domainRows, endpointRows);
            AdminHintSummaryDto hintSummary = HintEngine.Summarize(allHints);

            bool noData = domains.Count == 0 || req == 0;

            return new WindowStatsDto
            {
                Status = MetricsStoreStatusCodes.Connected,
                Range = window.RangeLabel,
                FromUtc = window.Start,
                ToUtc = window.End,
                QueriedAtUtc = now,
                StatsWindow = statsWindow,
                TotalRequests = req,
                TotalInvalidations = domains.Values.Sum(d => d.Invalidations),
                OcHitShare = oc.HitShare,
                FcHitShare = fc.HitShare,
                FactoryShare = fc.FactoryShare,
                Pipeline = pipe,
                Impact = impact,
                Domains = domainRows,
                Endpoints = endpointRows,
                NoData = noData,
                HintSummary = hintSummary,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or System.Text.Json.JsonException)
        {
            return Empty(MetricsStoreStatusCodes.Disconnected, window, now, statsWindow, ex.Message);
        }
    }

    private async Task<IReadOnlyList<PrometheusInstantSample>> QueryAsync(
        string promQl,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.QueryInstantAsync(promQl, at, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Count of events in the selected window for a counter (or histogram sum/count).
    /// Delegates to <see cref="MetricsPanelCatalog.BuildWindowCountPromQl"/>.
    /// </summary>
    private static string WindowCountBy(
        string byLabels,
        string metric,
        string rangeDuration,
        IReadOnlyList<string> domainFilter) =>
        MetricsPanelCatalog.BuildWindowCountPromQl(
            byLabels,
            metric + BuildDomainSelector(domainFilter),
            rangeDuration);

    private static string BuildDomainSelector(IReadOnlyList<string> domains)
    {
        if (domains.Count == 0)
            return "";
        List<string> parts = [];
        foreach (string d in domains)
        {
            string s = MetricsPanelCatalog.SanitizeLabelValue(d);
            if (s.Length > 0)
                parts.Add(s);
        }

        if (parts.Count == 0)
            return "";
        return "{domain=~\"" + string.Join("|", parts.Select(RegexEscape)) + "\"}";
    }

    private static string RegexEscape(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(".", "\\.", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static void AccumulateLayer(
        Dictionary<string, LayerBucket> map,
        IReadOnlyList<PrometheusInstantSample> samples,
        bool isOc,
        string keyLabel)
    {
        foreach (PrometheusInstantSample s in samples)
        {
            string key = Label(s.Metric, keyLabel);
            if (key.Length == 0)
                continue;
            ApplyResult(GetOrAdd(map, key), s, isOc);
        }
    }

    private static void AccumulateLayerByInstance(
        Dictionary<string, Dictionary<string, LayerBucket>> map,
        IReadOnlyList<PrometheusInstantSample> samples,
        bool isOc) =>
        AccumulateLayerByKeyInstance(map, samples, isOc, keyLabel: "domain");

    /// <summary>
    /// Groups samples by primary key label (domain or route) then <c>instance_id</c>.
    /// </summary>
    private static void AccumulateLayerByKeyInstance(
        Dictionary<string, Dictionary<string, LayerBucket>> map,
        IReadOnlyList<PrometheusInstantSample> samples,
        bool isOc,
        string keyLabel)
    {
        foreach (PrometheusInstantSample s in samples)
        {
            string key = Label(s.Metric, keyLabel);
            if (key.Length == 0)
                continue;
            string inst = InstanceId(s.Metric);
            if (!map.TryGetValue(key, out Dictionary<string, LayerBucket>? instMap))
            {
                instMap = new Dictionary<string, LayerBucket>(StringComparer.OrdinalIgnoreCase);
                map[key] = instMap;
            }

            ApplyResult(GetOrAdd(instMap, inst), s, isOc);
        }
    }

    private static void AccumulateFactoryDurationByKeyInstance(
        Dictionary<string, Dictionary<string, LayerBucket>> map,
        IReadOnlyList<PrometheusInstantSample> sums,
        IReadOnlyList<PrometheusInstantSample> counts,
        string keyLabel)
    {
        foreach (PrometheusInstantSample s in sums)
        {
            string key = Label(s.Metric, keyLabel);
            if (key.Length == 0 || s.Value is not double v || v <= 0)
                continue;
            string inst = InstanceId(s.Metric);
            if (!map.TryGetValue(key, out Dictionary<string, LayerBucket>? instMap))
            {
                instMap = new Dictionary<string, LayerBucket>(StringComparer.OrdinalIgnoreCase);
                map[key] = instMap;
            }

            GetOrAdd(instMap, inst).FactoryDurationSumMs += v;
        }

        foreach (PrometheusInstantSample s in counts)
        {
            string key = Label(s.Metric, keyLabel);
            if (key.Length == 0)
                continue;
            long n = ToCount(s.Value);
            if (n == 0)
                continue;
            string inst = InstanceId(s.Metric);
            if (!map.TryGetValue(key, out Dictionary<string, LayerBucket>? instMap))
            {
                instMap = new Dictionary<string, LayerBucket>(StringComparer.OrdinalIgnoreCase);
                map[key] = instMap;
            }

            GetOrAdd(instMap, inst).FactoryDurationCount += n;
        }
    }

    private static void AccumulateInv(
        Dictionary<string, LayerBucket> map,
        IReadOnlyList<PrometheusInstantSample> samples)
    {
        foreach (PrometheusInstantSample s in samples)
        {
            string key = Label(s.Metric, "domain");
            if (key.Length == 0)
                continue;
            long n = ToCount(s.Value);
            if (n == 0)
                continue;
            GetOrAdd(map, key).Invalidations += n;
        }
    }

    private static void AccumulateInvByInstance(
        Dictionary<string, Dictionary<string, LayerBucket>> map,
        IReadOnlyList<PrometheusInstantSample> samples)
    {
        foreach (PrometheusInstantSample s in samples)
        {
            string domain = Label(s.Metric, "domain");
            if (domain.Length == 0)
                continue;
            string inst = InstanceId(s.Metric);
            long n = ToCount(s.Value);
            if (n == 0)
                continue;
            if (!map.TryGetValue(domain, out Dictionary<string, LayerBucket>? instMap))
            {
                instMap = new Dictionary<string, LayerBucket>(StringComparer.OrdinalIgnoreCase);
                map[domain] = instMap;
            }

            GetOrAdd(instMap, inst).Invalidations += n;
        }
    }

    private static void AccumulateFactoryDuration(
        Dictionary<string, LayerBucket> map,
        IReadOnlyList<PrometheusInstantSample> sums,
        IReadOnlyList<PrometheusInstantSample> counts,
        string keyLabel)
    {
        foreach (PrometheusInstantSample s in sums)
        {
            string key = Label(s.Metric, keyLabel);
            if (key.Length == 0 || s.Value is not double v || v <= 0)
                continue;
            GetOrAdd(map, key).FactoryDurationSumMs += v;
        }

        foreach (PrometheusInstantSample s in counts)
        {
            string key = Label(s.Metric, keyLabel);
            if (key.Length == 0)
                continue;
            long n = ToCount(s.Value);
            if (n == 0)
                continue;
            GetOrAdd(map, key).FactoryDurationCount += n;
        }
    }

    private static void ApplyResult(LayerBucket b, PrometheusInstantSample s, bool isOc)
    {
        string result = Label(s.Metric, "result").ToLowerInvariant();
        long n = ToCount(s.Value);
        if (n == 0)
            return;

        if (isOc)
        {
            switch (result)
            {
                case "hit": b.OcHits += n; break;
                case "miss": b.OcMisses += n; break;
                case "bypass": b.OcBypass += n; break;
                default: b.OcBypass += n; break;
            }
        }
        else
        {
            switch (result)
            {
                case "hit": b.FcHits += n; break;
                case "miss":
                    b.FcMisses += n;
                    b.FactoryRuns += n;
                    break;
                case "stale":
                    b.FcStale += n;
                    b.FactoryFailures += n; // fail-safe after factory issues
                    break;
                case "fail":
                    b.FactoryFailures += n; // hard factory throw (OTEL)
                    break;
                case "bypass": b.FcBypass += n; break;
                default: b.FcBypass += n; break;
            }
        }
    }

    private static LayerBucket GetOrAdd(Dictionary<string, LayerBucket> map, string key)
    {
        if (!map.TryGetValue(key, out LayerBucket? b))
        {
            b = new LayerBucket();
            map[key] = b;
        }

        return b;
    }

    private static AdminDomainStatsDto ToDomain(
        string name,
        LayerBucket b,
        string? instanceId = null,
        string version = "",
        bool versionIsRuntimeOverride = false,
        IReadOnlyList<AdminDomainStatsDto>? byInstance = null,
        AdminInstanceSpreadDto? spread = null)
    {
        var (req, oc, fc, pipe) = b.BuildLayers();
        return new AdminDomainStatsDto
        {
            Name = name,
            InstanceId = instanceId,
            Version = version,
            VersionIsRuntimeOverride = versionIsRuntimeOverride,
            SchedulePhase = null,
            Invalidations = b.Invalidations,
            Requests = req,
            Oc = oc,
            Fc = fc,
            Pipeline = pipe,
            Impact = ImpactMath.Compute(req, b.FactoryRuns, b.FactoryDurationSumMs, b.FactoryDurationCount),
            Endpoints = [],
            Hints = [],
            ByInstance = byInstance,
            InstanceSpread = spread,
        };
    }

    private static AdminEndpointStatsDto ToEndpoint(
        string route,
        string? domain,
        LayerBucket b,
        string? instanceId = null,
        IReadOnlyList<AdminEndpointStatsDto>? byInstance = null,
        AdminInstanceSpreadDto? spread = null)
    {
        var (req, oc, fc, pipe) = b.BuildLayers();
        return new AdminEndpointStatsDto
        {
            Route = route,
            InstanceId = instanceId,
            ConfiguredDomain = string.IsNullOrEmpty(domain) ? null : domain,
            Requests = req,
            Oc = oc,
            Fc = fc,
            Pipeline = pipe,
            Impact = ImpactMath.Compute(req, b.FactoryRuns, b.FactoryDurationSumMs, b.FactoryDurationCount),
            ByInstance = byInstance,
            InstanceSpread = spread,
            Hints = [],
        };
    }

    private static string Label(IReadOnlyDictionary<string, string> metric, string name)
    {
        if (metric.TryGetValue(name, out string? v) && !string.IsNullOrWhiteSpace(v))
            return v.Trim();
        return "";
    }

    private static Dictionary<string, double> PeakMap(
        IReadOnlyList<PrometheusInstantSample> samples,
        string keyLabel)
    {
        Dictionary<string, double> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrometheusInstantSample s in samples)
        {
            string key = Label(s.Metric, keyLabel);
            if (key.Length == 0 || s.Value is not double v || double.IsNaN(v) || double.IsInfinity(v) || v <= 0)
                continue;
            map[key] = Math.Max(map.GetValueOrDefault(key), v);
        }

        return map;
    }

    /// <summary>
    /// Prefer scrape/resource label <c>instance_id</c>; missing → <see cref="MetricsWindow.UndefinedInstanceId"/>.
    /// </summary>
    private static string InstanceId(IReadOnlyDictionary<string, string> metric)
    {
        if (metric.TryGetValue(MetricsPanelCatalog.InstanceIdLabel, out string? v) && !string.IsNullOrWhiteSpace(v))
            return v.Trim();
        // Some scrapes only have Prometheus "instance" (host:port) — still better than collapsing silently.
        if (metric.TryGetValue("instance", out string? host) && !string.IsNullOrWhiteSpace(host))
            return host.Trim();
        return MetricsWindow.UndefinedInstanceId;
    }

    private static long ToCount(double? v)
    {
        if (v is not double d || double.IsNaN(d) || double.IsInfinity(d) || d <= 0)
            return 0;
        return (long)Math.Round(d);
    }

    private static IReadOnlyList<string> ParseCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv) || csv == "__none__")
            return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static WindowStatsDto Empty(
        string status,
        MetricsWindow window,
        DateTimeOffset now,
        string statsWindow,
        string? error) =>
        new()
        {
            Status = status,
            Range = window.RangeLabel,
            FromUtc = window.Start,
            ToUtc = window.End,
            QueriedAtUtc = now,
            Error = error,
            StatsWindow = statsWindow,
            Domains = [],
            Endpoints = [],
            NoData = true,
            HintSummary = new AdminHintSummaryDto(),
        };

    private sealed class LayerBucket
    {
        public long OcHits;
        public long OcMisses;
        public long OcBypass;
        public long FcHits;
        public long FcMisses;
        public long FcStale;
        public long FcBypass;
        public long FactoryRuns;
        public long FactoryFailures;
        public long Invalidations;
        public double FactoryDurationSumMs;
        public long FactoryDurationCount;

        public long Requests =>
            AdminStatsMath.Requests(OcHits, OcMisses, OcBypass, FcHits, FcMisses, FcStale, FcBypass);

        public LayerBucket Add(LayerBucket o)
        {
            OcHits += o.OcHits;
            OcMisses += o.OcMisses;
            OcBypass += o.OcBypass;
            FcHits += o.FcHits;
            FcMisses += o.FcMisses;
            FcStale += o.FcStale;
            FcBypass += o.FcBypass;
            FactoryRuns += o.FactoryRuns;
            FactoryFailures += o.FactoryFailures;
            Invalidations += o.Invalidations;
            FactoryDurationSumMs += o.FactoryDurationSumMs;
            FactoryDurationCount += o.FactoryDurationCount;
            return this;
        }

        public (long Requests, AdminLayerDto Oc, AdminFusionLayerDto Fc, AdminPipelineDto Pipeline) BuildLayers() =>
            AdminStatsMath.BuildAll(
                OcHits, OcMisses, OcBypass,
                FcHits, FcMisses, FcStale, FcBypass,
                FactoryRuns, FactoryFailures);
    }
}
