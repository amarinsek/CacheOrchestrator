using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Services.Hints;

namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>
/// Builds the Live page snapshot: fixed short-lookback Prometheus rates + instance health.
/// Independent of the global Range picker.
/// </summary>
public sealed class LiveStatsService
{
    /// <summary>Fixed rate window for live rates (not the Console Range).</summary>
    public const string DefaultLookback = "1m";

    private readonly IMetricsQueryClient _client;
    private readonly MetricsQueryService _metrics;
    private readonly AdminFanOutService _fanOut;
    private readonly HintEngine _hints;
    private readonly TimeProvider _time;

    public LiveStatsService(
        IMetricsQueryClient client,
        MetricsQueryService metrics,
        AdminFanOutService fanOut,
        HintEngine hints,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(fanOut);
        ArgumentNullException.ThrowIfNull(hints);
        ArgumentNullException.ThrowIfNull(time);
        _client = client;
        _metrics = metrics;
        _fanOut = fanOut;
        _hints = hints;
        _time = time;
    }

    public async Task<LiveSnapshotDto> GetAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        string lookback = DefaultLookback;

        Task<IReadOnlyList<InstanceStatusDto>> instancesTask =
            _fanOut.GetInstancesAsync(cancellationToken);
        Task<MetricsStatusDto> metricsTask =
            _metrics.GetStatusAsync(probe: true, cancellationToken);
        Task<FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>>> cfgTask =
            _fanOut.GetDomainsAsync(cancellationToken);

        // Hints: evaluate HintEngine on synthetic stats from live rates (no WindowStats nest).

        IReadOnlyList<InstanceStatusDto> instances = await instancesTask.ConfigureAwait(false);
        MetricsStatusDto metricsStatus = await metricsTask.ConfigureAwait(false);

        int healthy = instances.Count(i => i.Status == InstanceHealthStatus.Healthy);
        int degraded = instances.Count(i => i.Status == InstanceHealthStatus.Degraded);
        int down = instances.Count(i => i.Status == InstanceHealthStatus.Down);

        if (metricsStatus.Status != MetricsStoreStatusCodes.Connected)
        {
            return Empty(
                metricsStatus.Status,
                lookback,
                now,
                metricsStatus,
                instances,
                healthy,
                degraded,
                down,
                metricsStatus.Error);
        }

        try
        {
            string oc = MetricsPanelCatalog.OcRequests;
            string dc = MetricsPanelCatalog.DcRequests;
            string factoryRuns = MetricsPanelCatalog.FactoryRuns;
            string factoryFailures = MetricsPanelCatalog.FactoryFailures;
            string inv = MetricsPanelCatalog.Invalidations;
            string lb = lookback;

            Task<IReadOnlyList<PrometheusInstantSample>> clusterOc =
                Q($"sum(rate({oc}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> clusterOutputCacheHit =
                Q($"sum(rate({oc}{{result=\"hit\"}}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> clusterDataCacheHit =
                Q($"sum(rate({dc}{{result=\"hit\"}}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> clusterFac =
                Q($"sum(rate({factoryRuns}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> clusterFail =
                Q($"sum(rate({factoryFailures}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> clusterInv =
                Q($"sum(rate({inv}[{lb}]))", now, cancellationToken);

            Task<IReadOnlyList<PrometheusInstantSample>> domOc =
                Q($"sum by (domain) (rate({oc}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> domOutputCacheHit =
                Q($"sum by (domain) (rate({oc}{{result=\"hit\"}}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> domDataCacheHit =
                Q($"sum by (domain) (rate({dc}{{result=\"hit\"}}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> domFac =
                Q($"sum by (domain) (rate({factoryRuns}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> domFail =
                Q($"sum by (domain) (rate({factoryFailures}[{lb}]))", now, cancellationToken);

            Task<IReadOnlyList<PrometheusInstantSample>> epOc =
                Q($"sum by (route,domain) (rate({oc}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> epOutputCacheHit =
                Q($"sum by (route,domain) (rate({oc}{{result=\"hit\"}}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> epDataCacheHit =
                Q($"sum by (route,domain) (rate({dc}{{result=\"hit\"}}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> epFac =
                Q($"sum by (route,domain) (rate({factoryRuns}[{lb}]))", now, cancellationToken);
            Task<IReadOnlyList<PrometheusInstantSample>> epFail =
                Q($"sum by (route,domain) (rate({factoryFailures}[{lb}]))", now, cancellationToken);

            Task<IReadOnlyList<PrometheusInstantSample>> instOc =
                Q($"sum by (instance_id) (rate({oc}[{lb}]))", now, cancellationToken);

            await Task.WhenAll(
                    clusterOc, clusterOutputCacheHit, clusterDataCacheHit, clusterFac, clusterFail, clusterInv,
                    domOc, domOutputCacheHit, domDataCacheHit, domFac, domFail,
                    epOc, epOutputCacheHit, epDataCacheHit, epFac, epFail,
                    instOc, cfgTask)
                .ConfigureAwait(false);

            double? rps = FirstValue(await clusterOc.ConfigureAwait(false));
            double? outputCacheHit = FirstValue(await clusterOutputCacheHit.ConfigureAwait(false));
            double? dataCacheHit = FirstValue(await clusterDataCacheHit.ConfigureAwait(false));
            double? fac = FirstValue(await clusterFac.ConfigureAwait(false));
            double? fail = FirstValue(await clusterFail.ConfigureAwait(false));
            double? invRate = FirstValue(await clusterInv.ConfigureAwait(false));

            LiveClusterDto cluster = new()
            {
                HealthyCount = healthy,
                DegradedCount = degraded,
                DownCount = down,
                InstanceCount = instances.Count,
                RequestRate = rps,
                FactoryRate = fac,
                InvalidationRate = invRate,
                OutputCacheHitShare = Share(outputCacheHit, rps),
                DataCacheHitShare = Share(dataCacheHit, rps),
                FactoryShare = Share(fac, rps),
                FactoryFailShare = Share(fail, rps),
            };

            Dictionary<string, double> instRps = ToMap(await instOc.ConfigureAwait(false), "instance_id");
            var liveInstances = instances.Select(i =>
            {
                string key = i.ReportedInstanceId ?? i.Id;
                long? requests = null;
                if (instRps.TryGetValue(key, out double v) || instRps.TryGetValue(i.Id, out v))
                    requests = LiveHintProjector.EstimateRequests(v);
                return new InstanceStatusDto
                {
                    Id = i.Id,
                    Url = i.Url,
                    Status = i.Status,
                    ReportedInstanceId = i.ReportedInstanceId,
                    LatencyMs = i.LatencyMs,
                    StartedAtUtc = i.StartedAtUtc,
                    UptimeSeconds = i.UptimeSeconds,
                    Error = i.Error,
                    Requests = requests,
                    HintSummary = i.HintSummary,
                };
            }).ToList();

            Dictionary<string, RateBucket> domains = new(StringComparer.OrdinalIgnoreCase);
            MergeRate(domains, await domOc.ConfigureAwait(false), "domain", (b, v) => b.Rps += v);
            MergeRate(domains, await domOutputCacheHit.ConfigureAwait(false), "domain", (b, v) => b.OutputCacheHit += v);
            MergeRate(domains, await domDataCacheHit.ConfigureAwait(false), "domain", (b, v) => b.DataCacheHit += v);
            MergeRate(domains, await domFac.ConfigureAwait(false), "domain", (b, v) => b.Factory += v);
            MergeRate(domains, await domFail.ConfigureAwait(false), "domain", (b, v) => b.Fail += v);

            var domainRates = domains
                .Where(kv => !string.IsNullOrEmpty(kv.Key) && kv.Key is not "_" && !kv.Key.Contains('/', StringComparison.Ordinal))
                .Select(kv => ToEntity(kv.Key, domain: null, kv.Value))
                .Where(e => e.RequestRate > 0)
                .OrderByDescending(e => e.RequestRate)
                .ToList();

            Dictionary<string, RateBucket> endpoints = new(StringComparer.Ordinal);
            Dictionary<string, string> epDomain = new(StringComparer.Ordinal);
            foreach (PrometheusInstantSample s in await epOc.ConfigureAwait(false))
            {
                string route = PrometheusSampleHelpers.Label(s.Metric, "route");
                if (route.Length == 0 || s.Value is not double v || v <= 0)
                    continue;
                Get(endpoints, route).Rps += v;
                string dom = PrometheusSampleHelpers.Label(s.Metric, "domain");
                if (dom.Length > 0)
                    epDomain.TryAdd(route, dom);
            }

            MergeRate(endpoints, await epOutputCacheHit.ConfigureAwait(false), "route", (b, v) => b.OutputCacheHit += v);
            MergeRate(endpoints, await epDataCacheHit.ConfigureAwait(false), "route", (b, v) => b.DataCacheHit += v);
            MergeRate(endpoints, await epFac.ConfigureAwait(false), "route", (b, v) => b.Factory += v);
            MergeRate(endpoints, await epFail.ConfigureAwait(false), "route", (b, v) => b.Fail += v);

            // All live endpoints (client applies search/sort); no artificial top-N cut.
            var endpointRates = endpoints
                .Select(kv => ToEntity(kv.Key, epDomain.GetValueOrDefault(kv.Key), kv.Value))
                .Where(e => e.RequestRate > 0)
                .OrderByDescending(e => e.RequestRate)
                .ToList();

            HashSet<string> hotDomains = new(domainRates.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            List<string> quietNames = [];
            FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>> cfg =
                await cfgTask.ConfigureAwait(false);
            foreach (AdminDomainConfigDto c in cfg.Data ?? [])
            {
                if (!hotDomains.Contains(c.Name))
                    quietNames.Add(c.Name);
            }

            quietNames.Sort(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, AdminDomainConfigDto> configByName = new(StringComparer.OrdinalIgnoreCase);
            foreach (AdminDomainConfigDto c in cfg.Data ?? [])
                configByName.TryAdd(c.Name, c);

            AdminHintSummaryDto hintSummary = LiveHintProjector.Evaluate(
                _hints, domainRates, endpointRates, quietNames, configByName);

            var domainRows = domainRates.Select(e =>
            {
                configByName.TryGetValue(e.Name, out AdminDomainConfigDto? cfgDto);
                return _hints.WithHints(LiveHintProjector.ToDomainStats(e, cfgDto), cfgDto);
            }).ToList();

            var endpointRows = endpointRates
                .Select(e => _hints.WithHints(LiveHintProjector.ToEndpointStats(e)))
                .ToList();

            List<AdminDomainStatsDto> quietRows = [];
            foreach (string name in quietNames)
            {
                if (!configByName.TryGetValue(name, out AdminDomainConfigDto? cfgDto))
                    continue;
                quietRows.Add(_hints.WithHints(LiveHintProjector.ToQuietDomainStats(cfgDto), cfgDto));
            }

            return new LiveSnapshotDto
            {
                Status = MetricsStoreStatusCodes.Connected,
                Lookback = lookback,
                QueriedAtUtc = now,
                Metrics = metricsStatus,
                Cluster = cluster,
                Pipeline = LiveHintProjector.ToClusterPipeline(cluster),
                Instances = liveInstances,
                Domains = domainRows,
                Endpoints = endpointRows,
                QuietDomains = quietRows,
                HintSummary = hintSummary,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or System.Text.Json.JsonException)
        {
            return Empty(
                MetricsStoreStatusCodes.Disconnected,
                lookback,
                now,
                metricsStatus,
                instances,
                healthy,
                degraded,
                down,
                ex.Message);
        }
    }

    private async Task<IReadOnlyList<PrometheusInstantSample>> Q(
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

    private static LiveSnapshotDto Empty(
        string status,
        string lookback,
        DateTimeOffset now,
        MetricsStatusDto? metrics,
        IReadOnlyList<InstanceStatusDto> instances,
        int healthy,
        int degraded,
        int down,
        string? error) =>
        new()
        {
            Status = status,
            Lookback = lookback,
            QueriedAtUtc = now,
            Error = error,
            Metrics = metrics,
            Cluster = new LiveClusterDto
            {
                HealthyCount = healthy,
                DegradedCount = degraded,
                DownCount = down,
                InstanceCount = instances.Count,
            },
            Instances = instances.ToArray(),
            Domains = [],
            Endpoints = [],
            QuietDomains = [],
            HintSummary = new AdminHintSummaryDto(),
        };

    private static LiveEntityRateDto ToEntity(string name, string? domain, RateBucket b) =>
        new()
        {
            Name = name,
            Domain = domain,
            RequestRate = b.Rps,
            OutputCacheHitShare = Share(b.OutputCacheHit, b.Rps),
            DataCacheHitShare = Share(b.DataCacheHit, b.Rps),
            FactoryShare = Share(b.Factory, b.Rps),
            FactoryFailShare = Share(b.Fail, b.Rps),
        };

    private static double? Share(double? part, double? total) =>
        total is > 1e-12 && part is double p ? Math.Clamp(p / total.Value, 0, 1) : null;

    private static double? FirstValue(IReadOnlyList<PrometheusInstantSample> samples) =>
        PrometheusSampleHelpers.FirstValue(samples);

    private static Dictionary<string, double> ToMap(
        IReadOnlyList<PrometheusInstantSample> samples,
        string label)
    {
        Dictionary<string, double> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrometheusInstantSample s in samples)
        {
            string key = PrometheusSampleHelpers.Label(s.Metric, label);
            if (key.Length == 0 || s.Value is not double v || v <= 0 || double.IsNaN(v))
                continue;
            map[key] = map.GetValueOrDefault(key) + v;
        }

        return map;
    }

    private static void MergeRate(
        Dictionary<string, RateBucket> map,
        IReadOnlyList<PrometheusInstantSample> samples,
        string keyLabel,
        Action<RateBucket, double> apply)
    {
        foreach (PrometheusInstantSample s in samples)
        {
            string key = PrometheusSampleHelpers.Label(s.Metric, keyLabel);
            if (key.Length == 0 || s.Value is not double v || v <= 0 || double.IsNaN(v))
                continue;
            apply(Get(map, key), v);
        }
    }

    private static RateBucket Get(Dictionary<string, RateBucket> map, string key)
    {
        if (!map.TryGetValue(key, out RateBucket? b))
        {
            b = new RateBucket();
            map[key] = b;
        }

        return b;
    }

    private sealed class RateBucket
    {
        public double Rps;
        public double OutputCacheHit;
        public double DataCacheHit;
        public double Factory;
        public double Fail;
    }
}
