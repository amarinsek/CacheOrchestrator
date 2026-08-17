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

            // Window counts: current − value@window_start (missing start → full current).
            // Do NOT use increase(): it skips the first sample of a new series, so the first
            // request(s) after an empty Prom stay at 0 forever until more scrapes arrive.
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
            Task<FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>>> cfgTask =
                _fanOut.GetDomainsAsync(cancellationToken);

            await Task.WhenAll(
                    ocTask, fcTask, invTask, facSumTask, facCntTask,
                    ocRouteTask, fcRouteTask, facSumRouteTask, facCntRouteTask,
                    ocInstTask, fcInstTask, invInstTask, cfgTask)
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

                List<AdminDomainStatsDto>? byInstance = null;
                AdminInstanceSpreadDto? spread = null;
                if (domainInst.TryGetValue(name, out Dictionary<string, LayerBucket>? instMap) && instMap.Count > 0)
                {
                    byInstance = instMap
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(kv => ToDomain(name, kv.Value, instanceId: kv.Key))
                        .ToList();
                    spread = new AdminInstanceSpreadDto
                    {
                        OcHitShare = AdminStatsMath.Spread(byInstance.Select(x => x.Oc.HitShare)),
                        FcHitShare = AdminStatsMath.Spread(byInstance.Select(x => x.Fc.HitShare)),
                        FactoryShare = AdminStatsMath.Spread(byInstance.Select(x => x.Fc.FactoryShare)),
                    };
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
                routeDomain.TryGetValue(route, out string? dom);
                AdminEndpointStatsDto ep = ToEndpoint(route, dom, b);
                endpointRows.Add(_hints.WithHints(ep));
            }

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
    /// Count of events in the window for a counter (or histogram sum/count).
    /// <list type="bullet">
    /// <item>If the series existed at window start: <c>now - then</c> (clamped ≥ 0).</item>
    /// <item>If the series is new in the window: full current value (includes the first request).</item>
    /// </list>
    /// Using <c>increase()</c> alone under-counts because Prom skips the first sample of a new series.
    /// </summary>
    private static string WindowCountBy(
        string byLabels,
        string metric,
        string rangeDuration,
        IReadOnlyList<string> domainFilter)
    {
        string sel = BuildDomainSelector(domainFilter);
        string m = metric + sel;
        // offset must match the selected window (e.g. 900s for 15m, 3600s for 1h).
        string offset = rangeDuration;
        return
            "clamp_min((" +
            $"sum by ({byLabels}) ({m})" +
            $" - sum by ({byLabels}) ({m} offset {offset})" +
            $") or sum by ({byLabels}) ({m} unless on ({byLabels}) {m} offset {offset}), 0)";
    }

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
        bool isOc)
    {
        foreach (PrometheusInstantSample s in samples)
        {
            string domain = Label(s.Metric, "domain");
            if (domain.Length == 0)
                continue;
            string inst = InstanceId(s.Metric);
            if (!map.TryGetValue(domain, out Dictionary<string, LayerBucket>? instMap))
            {
                instMap = new Dictionary<string, LayerBucket>(StringComparer.OrdinalIgnoreCase);
                map[domain] = instMap;
            }

            ApplyResult(GetOrAdd(instMap, inst), s, isOc);
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
            if (key.Length == 0 || s.Value is not double v)
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
                case "stale": b.FcStale += n; break;
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

    private static AdminEndpointStatsDto ToEndpoint(string route, string? domain, LayerBucket b)
    {
        var (req, oc, fc, pipe) = b.BuildLayers();
        return new AdminEndpointStatsDto
        {
            Route = route,
            ConfiguredDomain = string.IsNullOrEmpty(domain) ? null : domain,
            Requests = req,
            Oc = oc,
            Fc = fc,
            Pipeline = pipe,
            Impact = ImpactMath.Compute(req, b.FactoryRuns, b.FactoryDurationSumMs, b.FactoryDurationCount),
            Hints = [],
        };
    }

    private static string Label(IReadOnlyDictionary<string, string> metric, string name)
    {
        if (metric.TryGetValue(name, out string? v) && !string.IsNullOrWhiteSpace(v))
            return v.Trim();
        return "";
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
            Invalidations += o.Invalidations;
            FactoryDurationSumMs += o.FactoryDurationSumMs;
            FactoryDurationCount += o.FactoryDurationCount;
            return this;
        }

        public (long Requests, AdminLayerDto Oc, AdminFusionLayerDto Fc, AdminPipelineDto Pipeline) BuildLayers() =>
            AdminStatsMath.BuildAll(
                OcHits, OcMisses, OcBypass,
                FcHits, FcMisses, FcStale, FcBypass,
                FactoryRuns, factoryFailures: 0);
    }
}
