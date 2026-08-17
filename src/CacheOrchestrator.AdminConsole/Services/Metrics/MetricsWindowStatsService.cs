using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;

namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>
/// Builds domain/endpoint stats from Prometheus for a selected time window
/// so the Console can show windowed traffic without Local Admin process totals.
/// </summary>
public sealed class MetricsWindowStatsService
{
    private readonly IMetricsQueryClient _client;
    private readonly MetricsQueryService _status;
    private readonly TimeProvider _time;

    public MetricsWindowStatsService(
        IMetricsQueryClient client,
        MetricsQueryService status,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(time);
        _client = client;
        _status = status;
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

            // Parallel instant queries evaluated at window end.
            Task<IReadOnlyList<PrometheusInstantSample>> ocTask = QueryAsync(
                IncreaseBy("domain,result", MetricsPanelCatalog.OcRequests, rw, domainFilter, byRoute: false),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> fcTask = QueryAsync(
                IncreaseBy("domain,result", MetricsPanelCatalog.FcRequests, rw, domainFilter, byRoute: false),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> invTask = QueryAsync(
                IncreaseBy("domain", MetricsPanelCatalog.Invalidations, rw, domainFilter, byRoute: false),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facSumTask = QueryAsync(
                IncreaseBy("domain", MetricsPanelCatalog.FactoryDurationSum, rw, domainFilter, byRoute: false),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facCntTask = QueryAsync(
                IncreaseBy("domain", MetricsPanelCatalog.FactoryDurationCount, rw, domainFilter, byRoute: false),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> ocRouteTask = QueryAsync(
                IncreaseBy("route,result,domain", MetricsPanelCatalog.OcRequests, rw, domainFilter, byRoute: true),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> fcRouteTask = QueryAsync(
                IncreaseBy("route,result,domain", MetricsPanelCatalog.FcRequests, rw, domainFilter, byRoute: true),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facSumRouteTask = QueryAsync(
                IncreaseBy("route,domain", MetricsPanelCatalog.FactoryDurationSum, rw, domainFilter, byRoute: true),
                window.End, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> facCntRouteTask = QueryAsync(
                IncreaseBy("route,domain", MetricsPanelCatalog.FactoryDurationCount, rw, domainFilter, byRoute: true),
                window.End, cancellationToken);

            await Task.WhenAll(
                    ocTask, fcTask, invTask, facSumTask, facCntTask,
                    ocRouteTask, fcRouteTask, facSumRouteTask, facCntRouteTask)
                .ConfigureAwait(false);

            Dictionary<string, LayerBucket> domains = new(StringComparer.OrdinalIgnoreCase);
            AccumulateLayer(domains, await ocTask.ConfigureAwait(false), isOc: true, keyLabel: "domain");
            AccumulateLayer(domains, await fcTask.ConfigureAwait(false), isOc: false, keyLabel: "domain");
            AccumulateInv(domains, await invTask.ConfigureAwait(false));
            AccumulateFactoryDuration(domains, await facSumTask.ConfigureAwait(false), await facCntTask.ConfigureAwait(false), keyLabel: "domain");

            IReadOnlyList<PrometheusInstantSample> ocRoute = await ocRouteTask.ConfigureAwait(false);
            IReadOnlyList<PrometheusInstantSample> fcRoute = await fcRouteTask.ConfigureAwait(false);
            Dictionary<string, LayerBucket> routes = new(StringComparer.Ordinal);
            AccumulateLayer(routes, ocRoute, isOc: true, keyLabel: "route");
            AccumulateLayer(routes, fcRoute, isOc: false, keyLabel: "route");
            AccumulateFactoryDuration(routes, await facSumRouteTask.ConfigureAwait(false), await facCntRouteTask.ConfigureAwait(false), keyLabel: "route");
            // Map route → configured domain from first sample label
            Dictionary<string, string> routeDomain = new(StringComparer.Ordinal);
            foreach (PrometheusInstantSample s in ocRoute.Concat(fcRoute))
            {
                string route = Label(s.Metric, "route");
                string dom = Label(s.Metric, "domain");
                if (route.Length > 0 && dom.Length > 0 && !routeDomain.ContainsKey(route))
                    routeDomain[route] = dom;
            }

            List<AdminDomainStatsDto> domainRows = [];
            foreach ((string name, LayerBucket b) in domains.OrderByDescending(kv => kv.Value.Requests))
            {
                if (string.IsNullOrEmpty(name) || name is "_" or "undefined")
                    continue;
                domainRows.Add(ToDomain(name, b));
            }

            List<AdminEndpointStatsDto> endpointRows = [];
            foreach ((string route, LayerBucket b) in routes.OrderByDescending(kv => kv.Value.Requests))
            {
                if (string.IsNullOrEmpty(route))
                    continue;
                routeDomain.TryGetValue(route, out string? dom);
                endpointRows.Add(ToEndpoint(route, dom, b));
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

            domainRows = domainRows.Select(d => new AdminDomainStatsDto
            {
                Name = d.Name,
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
                Endpoints = byDom.TryGetValue(d.Name, out List<AdminEndpointStatsDto>? eps)
                    ? eps
                    : Array.Empty<AdminEndpointStatsDto>(),
            }).ToList();

            LayerBucket cluster = domains.Values.Aggregate(new LayerBucket(), (a, b) => a.Add(b));
            var (req, oc, fc, pipe) = cluster.BuildLayers();
            CacheImpactKpiDto impact = ImpactMath.Compute(
                req,
                cluster.FactoryRuns,
                cluster.FactoryDurationSumMs,
                cluster.FactoryDurationCount);

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
            // Missing metric (e.g. no route label / no factory histogram) → empty, not fatal.
            return [];
        }
    }

    private static string IncreaseBy(
        string byLabels,
        string metric,
        string rangeDuration,
        IReadOnlyList<string> domainFilter,
        bool byRoute)
    {
        string sel = BuildDomainSelector(domainFilter);
        // increase over the full window; sum by labels (missing labels coalesce later).
        return $"sum by ({byLabels}) (increase({metric}{sel}[{rangeDuration}]))";
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
            string result = Label(s.Metric, "result").ToLowerInvariant();
            long n = ToCount(s.Value);
            if (n == 0)
                continue;

            if (!map.TryGetValue(key, out LayerBucket? b))
            {
                b = new LayerBucket();
                map[key] = b;
            }

            if (isOc)
            {
                switch (result)
                {
                    case "hit": b.OcHits += n; break;
                    case "miss": b.OcMisses += n; break;
                    case "bypass": b.OcBypass += n; break;
                    default:
                        // unknown result → count as bypass-ish other for denominator
                        b.OcBypass += n;
                        break;
                }
            }
            else
            {
                switch (result)
                {
                    case "hit": b.FcHits += n; break;
                    case "miss":
                        b.FcMisses += n;
                        b.FactoryRuns += n; // factory path ≈ FC miss
                        break;
                    case "stale": b.FcStale += n; break;
                    case "bypass": b.FcBypass += n; break;
                    default:
                        b.FcBypass += n;
                        break;
                }
            }
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
            if (!map.TryGetValue(key, out LayerBucket? b))
            {
                b = new LayerBucket();
                map[key] = b;
            }

            b.Invalidations += n;
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
            if (!map.TryGetValue(key, out LayerBucket? b))
            {
                b = new LayerBucket();
                map[key] = b;
            }

            b.FactoryDurationSumMs += v;
        }

        foreach (PrometheusInstantSample s in counts)
        {
            string key = Label(s.Metric, keyLabel);
            if (key.Length == 0)
                continue;
            long n = ToCount(s.Value);
            if (n == 0)
                continue;
            if (!map.TryGetValue(key, out LayerBucket? b))
            {
                b = new LayerBucket();
                map[key] = b;
            }

            b.FactoryDurationCount += n;
        }
    }

    private static AdminDomainStatsDto ToDomain(string name, LayerBucket b)
    {
        var (req, oc, fc, pipe) = b.BuildLayers();
        return new AdminDomainStatsDto
        {
            Name = name,
            Version = "", // current value filled by config overlay in UI when needed
            VersionIsRuntimeOverride = false,
            SchedulePhase = null,
            Invalidations = b.Invalidations,
            Requests = req,
            Oc = oc,
            Fc = fc,
            Pipeline = pipe,
            Impact = ImpactMath.Compute(req, b.FactoryRuns, b.FactoryDurationSumMs, b.FactoryDurationCount),
            Endpoints = [],
            Hints = [],
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
        // Instance scrape labels sometimes use different keys — not used as domain/route keys here.
        return "";
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
