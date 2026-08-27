using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>
/// Admin Console App BFF over <see cref="IMetricsQueryClient"/>: status, catalog, series, summary.
/// Never throws for “not configured”; returns status envelopes for the SPA.
/// </summary>
public sealed class MetricsQueryService
{
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromSeconds(15);

    private readonly IMetricsQueryClient _client;
    private readonly AdminConsoleOptions _options;
    private readonly TimeProvider _time;
    private readonly object _probeGate = new();
    private MetricsStatusDto? _probeCache;
    private DateTimeOffset _probeExpiresUtc;

    public MetricsQueryService(
        IMetricsQueryClient client,
        IOptions<AdminConsoleOptions> options,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _client = client;
        _options = options.Value;
        _time = time;
    }

    /// <summary>Configuration + optional live probe (probes are cached briefly).</summary>
    public async Task<MetricsStatusDto> GetStatusAsync(
        bool probe = true,
        CancellationToken cancellationToken = default)
    {
        MetricsStoreOptions metrics = _options.Metrics;
        DateTimeOffset now = _time.GetUtcNow();

        if (!metrics.IsConfigured)
        {
            return new MetricsStatusDto
            {
                Status = MetricsStoreStatusCodes.NotConfigured,
                Provider = metrics.Provider,
                CheckedAtUtc = now,
                DefaultRange = MetricsRange.Normalize(metrics.DefaultRange),
                Error = "Set AdminConsole:Metrics:Enabled and BaseUrl to enable time series.",
            };
        }

        if (!PrometheusMetricsQueryClient.IsPrometheusProvider(metrics.Provider))
        {
            return new MetricsStatusDto
            {
                Status = MetricsStoreStatusCodes.Disconnected,
                Provider = metrics.Provider,
                Host = TryHost(metrics.BaseUrl),
                CheckedAtUtc = now,
                DefaultRange = MetricsRange.Normalize(metrics.DefaultRange),
                Error = $"Unsupported provider '{metrics.Provider}'. Supported: Prometheus.",
            };
        }

        if (!probe)
        {
            return new MetricsStatusDto
            {
                Status = MetricsStoreStatusCodes.Connected,
                Provider = metrics.Provider,
                Host = TryHost(metrics.BaseUrl),
                CheckedAtUtc = now,
                DefaultRange = MetricsRange.Normalize(metrics.DefaultRange),
            };
        }

        lock (_probeGate)
        {
            if (_probeCache is not null && _probeExpiresUtc > now)
                return _probeCache;
        }

        MetricsProbeResult result = await _client.ProbeAsync(cancellationToken).ConfigureAwait(false);
        MetricsStatusDto dto = new()
        {
            Status = result.Succeeded
                ? MetricsStoreStatusCodes.Connected
                : MetricsStoreStatusCodes.Disconnected,
            Provider = metrics.Provider,
            Host = TryHost(metrics.BaseUrl),
            CheckedAtUtc = now,
            LatencyMs = result.LatencyMs,
            Error = result.Succeeded ? null : result.Error,
            DefaultRange = MetricsRange.Normalize(metrics.DefaultRange),
        };

        // Cache successes; allow a quick retry after a failed probe (Prom starting up, blip).
        if (result.Succeeded)
        {
            lock (_probeGate)
            {
                _probeCache = dto;
                _probeExpiresUtc = _time.GetUtcNow() + ProbeCacheTtl;
            }
        }

        return dto;
    }

    /// <summary>Allowlisted panel list (empty when not configured).</summary>
    public MetricsCatalogDto GetCatalog()
    {
        MetricsStoreOptions metrics = _options.Metrics;
        if (!metrics.IsConfigured)
        {
            return new MetricsCatalogDto
            {
                Status = MetricsStoreStatusCodes.NotConfigured,
                Panels = [],
            };
        }

        return new MetricsCatalogDto
        {
            Status = MetricsStoreStatusCodes.Connected,
            Panels = MetricsPanelCatalog.Panels,
        };
    }

    /// <summary>
    /// Loads one or more panels. <paramref name="panels"/> is a comma-separated list of panel ids;
    /// null/empty loads the default set for the Metrics page.
    /// Optional <paramref name="domains"/>, <paramref name="instances"/> (<c>instance_id</c> scrape label),
    /// and <paramref name="routes"/> (stable endpoint keys) filter PromQL.
    /// </summary>
    public async Task<MetricsSeriesResponseDto> GetSeriesAsync(
        string? range,
        string? panels,
        string? domains,
        string? instances = null,
        string? routes = null,
        string? from = null,
        string? to = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        var window = MetricsWindow.Resolve(range, from, to, now);

        MetricsStatusDto status = await GetStatusAsync(probe: true, cancellationToken).ConfigureAwait(false);
        if (status.Status != MetricsStoreStatusCodes.Connected)
        {
            return new MetricsSeriesResponseDto
            {
                Status = status.Status,
                Range = window.RangeLabel,
                Step = window.Step,
                FromUtc = window.Start,
                ToUtc = window.End,
                QueriedAtUtc = now,
                Error = status.Error,
                Panels = [],
            };
        }

        IReadOnlyList<string> panelIds = ParsePanelList(panels);
        IReadOnlyList<string> domainList = ParseCsv(domains);
        IReadOnlyList<string> instanceList = ParseCsv(instances);
        IReadOnlyList<string> routeList = ParseRouteCsv(routes);
        DateTimeOffset start = window.Start;
        DateTimeOffset end = window.End;
        string step = window.Step;
        bool routeScoped = routeList.Count > 0;

        Task<MetricsPanelDto>[] panelTasks = panelIds
            .Select(panelId => LoadPanelAsync(
                panelId, domainList, instanceList, routeList, start, end, step, routeScoped, cancellationToken))
            .ToArray();
        MetricsPanelDto[] results = await Task.WhenAll(panelTasks).ConfigureAwait(false);

        return new MetricsSeriesResponseDto
        {
            Status = MetricsStoreStatusCodes.Connected,
            Range = window.RangeLabel,
            Step = step,
            FromUtc = window.Start,
            ToUtc = window.End,
            QueriedAtUtc = now,
            Panels = results,
        };
    }

    private async Task<MetricsPanelDto> LoadPanelAsync(
        string panelId,
        IReadOnlyList<string> domainList,
        IReadOnlyList<string> instanceList,
        IReadOnlyList<string> routeList,
        DateTimeOffset start,
        DateTimeOffset end,
        string step,
        bool routeScoped,
        CancellationToken cancellationToken)
    {
        MetricsPanelInfoDto? info = MetricsPanelCatalog.Find(panelId);
        if (info is null)
        {
            return new MetricsPanelDto
            {
                Id = panelId,
                Title = panelId,
                Description = null,
                Unit = "rate",
                Series = [],
                Warning = $"Unknown panel '{panelId}'.",
            };
        }

        try
        {
            string promQl = MetricsPanelCatalog.BuildPromQl(info.Id, domainList, instanceList, routeList);
            IReadOnlyList<PrometheusMatrixSeries> matrix = await _client
                .QueryRangeAsync(promQl, start, end, step, cancellationToken)
                .ConfigureAwait(false);

            var series = matrix
                .Select(m => new MetricsSeriesDto
                {
                    Name = SeriesName(info.Id, m.Metric),
                    Labels = m.Metric,
                    Points = m.Points,
                })
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new MetricsPanelDto
            {
                Id = info.Id,
                Title = info.Title,
                Description = info.Description,
                Unit = info.Unit,
                Series = series,
                Warning = series.Count == 0
                    ? EmptySeriesWarning(routeScoped)
                    : null,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException)
        {
            return new MetricsPanelDto
            {
                Id = info.Id,
                Title = info.Title,
                Description = info.Description,
                Unit = info.Unit,
                Series = [],
                Warning = ex.Message,
            };
        }
    }

    private static string EmptySeriesWarning(bool routeScoped) =>
        routeScoped
            ? "No samples for this route in the selected range. Possible causes: no traffic, " +
              "Cache:Metrics:IncludeEndpointLabel off on some/all instances during this window, " +
              "or scrape labels do not match."
            : "No samples in this range (is the CacheOrchestrator meter scraped?).";

    /// <summary>Instant KPI snapshot for the Metrics toolbar.</summary>
    public async Task<MetricsSummaryDto> GetSummaryAsync(
        string? range,
        string? from = null,
        string? to = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        var window = MetricsWindow.Resolve(range, from, to, now);
        // Summary rate() windows use a relative token nearest the absolute duration.
        string rateWindow = window.IsAbsolute
            ? MetricsRange.NearestToken(window.End - window.Start)
            : window.RangeLabel;

        MetricsStatusDto status = await GetStatusAsync(probe: true, cancellationToken).ConfigureAwait(false);
        if (status.Status != MetricsStoreStatusCodes.Connected)
        {
            return new MetricsSummaryDto
            {
                Status = status.Status,
                Range = window.RangeLabel,
                FromUtc = window.Start,
                ToUtc = window.End,
                QueriedAtUtc = now,
                Error = status.Error,
            };
        }

        try
        {
            Task<double?> requestRateTask = InstantValueAsync("request_rate", rateWindow, cancellationToken);
            Task<double?> outputCacheHitTask = InstantValueAsync("oc_hit_share", rateWindow, cancellationToken);
            Task<double?> dataCacheHitTask = InstantValueAsync("dc_hit_rate", rateWindow, cancellationToken);
            Task<double?> invTask = InstantValueAsync("invalidation_rate", rateWindow, cancellationToken);
            Task<double?> factoryShareTask = InstantValueAsync("factory_share", rateWindow, cancellationToken);

            await Task.WhenAll(requestRateTask, outputCacheHitTask, dataCacheHitTask, invTask, factoryShareTask)
                .ConfigureAwait(false);

            double? requestRate = await requestRateTask.ConfigureAwait(false);
            double? outputCacheHit = await outputCacheHitTask.ConfigureAwait(false);
            double? dataCacheHit = await dataCacheHitTask.ConfigureAwait(false);
            double? inv = await invTask.ConfigureAwait(false);
            double? factoryShare = await factoryShareTask.ConfigureAwait(false);

            bool noData = requestRate is null && outputCacheHit is null && dataCacheHit is null && inv is null && factoryShare is null;

            return new MetricsSummaryDto
            {
                Status = MetricsStoreStatusCodes.Connected,
                Range = window.RangeLabel,
                FromUtc = window.Start,
                ToUtc = window.End,
                QueriedAtUtc = now,
                RequestRate = requestRate,
                OutputCacheHitShare = outputCacheHit,
                DataCacheHitRate = dataCacheHit,
                InvalidationRate = inv,
                FactoryShare = factoryShare,
                NoData = noData,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return new MetricsSummaryDto
            {
                Status = MetricsStoreStatusCodes.Disconnected,
                Range = window.RangeLabel,
                FromUtc = window.Start,
                ToUtc = window.End,
                QueriedAtUtc = now,
                Error = ex.Message,
            };
        }
    }

    private async Task<double?> InstantValueAsync(
        string panelId,
        string rangeWindow,
        CancellationToken cancellationToken)
    {
        string promQl = MetricsPanelCatalog.BuildSummaryPromQl(panelId, rangeWindow);
        IReadOnlyList<PrometheusInstantSample> samples = await _client
            .QueryInstantAsync(promQl, timeUtc: null, cancellationToken)
            .ConfigureAwait(false);
        return samples.FirstOrDefault()?.Value;
    }

    private static IReadOnlyList<string> ParsePanelList(string? panels)
    {
        if (string.IsNullOrWhiteSpace(panels))
            return MetricsPanelCatalog.DefaultPagePanels.ToList();

        List<string> list = [];
        foreach (string part in panels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (MetricsPanelCatalog.Find(part) is not null && !list.Contains(part, StringComparer.OrdinalIgnoreCase))
                list.Add(part);
        }

        return list.Count > 0 ? list : MetricsPanelCatalog.DefaultPagePanels.ToList();
    }

    private static IReadOnlyList<string> ParseCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MetricsPanelCatalog.SanitizeLabelValue)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ParseRouteCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];
        // Single route may contain commas rarely; treat whole string as one if no multi-encode.
        // Prefer one route per query (endpoint detail).
        string one = MetricsPanelCatalog.SanitizeRouteLabelValue(csv);
        if (one.Length > 0 && !csv.Contains(',', StringComparison.Ordinal))
            return [one];

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MetricsPanelCatalog.SanitizeRouteLabelValue)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string SeriesName(string panelId, IReadOnlyDictionary<string, string> metric)
    {
        if (panelId is "schedule_phase" && metric.TryGetValue("phase", out string? phase) && phase.Length > 0)
            return phase;
        if (panelId is "cluster_publish_failures" && metric.TryGetValue("reason", out string? reason) && reason.Length > 0)
            return reason;
        if (metric.TryGetValue("route", out string? route) && route.Length > 0)
            return route;
        if (metric.TryGetValue("domain", out string? domain) && domain.Length > 0)
            return domain;
        if (metric.TryGetValue("instance_id", out string? iid) && iid.Length > 0)
            return iid;
        return "cluster";
    }

    private static string? TryHost(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? uri))
            return baseUrl.Trim();
        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }
}
