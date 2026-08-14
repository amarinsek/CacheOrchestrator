using System.Text.Json;
using CacheOrchestrator.Admin.App.Models;
using CacheOrchestrator.Admin.App.Options;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Admin.App.Services.Metrics;

/// <summary>
/// Admin App BFF over <see cref="IMetricsQueryClient"/>: status, catalog, series, summary.
/// Never throws for “not configured”; returns status envelopes for the SPA.
/// </summary>
public sealed class MetricsQueryService
{
    private readonly IMetricsQueryClient _client;
    private readonly CacheAdminOptions _options;
    private readonly TimeProvider _time;

    public MetricsQueryService(
        IMetricsQueryClient client,
        IOptions<CacheAdminOptions> options,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _client = client;
        _options = options.Value;
        _time = time;
    }

    /// <summary>Configuration + optional live probe.</summary>
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
                Error = "Set CacheAdmin:Metrics:Enabled and BaseUrl to enable time series.",
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

        MetricsProbeResult result = await _client.ProbeAsync(cancellationToken).ConfigureAwait(false);
        return new MetricsStatusDto
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
    /// </summary>
    public async Task<MetricsSeriesResponseDto> GetSeriesAsync(
        string? range,
        string? panels,
        string? domains,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        string resolvedRange = MetricsRange.Normalize(range, _options.Metrics.DefaultRange);
        string step = MetricsRange.StepFor(resolvedRange);

        MetricsStatusDto status = await GetStatusAsync(probe: true, cancellationToken).ConfigureAwait(false);
        if (status.Status != MetricsStoreStatusCodes.Connected)
        {
            return new MetricsSeriesResponseDto
            {
                Status = status.Status,
                Range = resolvedRange,
                Step = step,
                QueriedAtUtc = now,
                Error = status.Error,
                Panels = [],
            };
        }

        IReadOnlyList<string> panelIds = ParsePanelList(panels);
        IReadOnlyList<string> domainList = ParseCsv(domains);
        DateTimeOffset start = now - MetricsRange.ToTimeSpan(resolvedRange);

        List<MetricsPanelDto> results = [];
        foreach (string panelId in panelIds)
        {
            MetricsPanelInfoDto? info = MetricsPanelCatalog.Find(panelId);
            if (info is null)
            {
                results.Add(new MetricsPanelDto
                {
                    Id = panelId,
                    Title = panelId,
                    Unit = "rate",
                    Series = [],
                    Warning = $"Unknown panel '{panelId}'.",
                });
                continue;
            }

            try
            {
                string promQl = MetricsPanelCatalog.BuildPromQl(info.Id, domainList);
                IReadOnlyList<PrometheusMatrixSeries> matrix = await _client
                    .QueryRangeAsync(promQl, start, now, step, cancellationToken)
                    .ConfigureAwait(false);

                List<MetricsSeriesDto> series = matrix
                    .Select(m => new MetricsSeriesDto
                    {
                        Name = SeriesName(info.Id, m.Metric),
                        Labels = m.Metric,
                        Points = m.Points,
                    })
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                results.Add(new MetricsPanelDto
                {
                    Id = info.Id,
                    Title = info.Title,
                    Unit = info.Unit,
                    Series = series,
                    Warning = series.Count == 0
                        ? "No samples in this range (is the CacheOrchestrator meter scraped?)."
                        : null,
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException)
            {
                results.Add(new MetricsPanelDto
                {
                    Id = info.Id,
                    Title = info.Title,
                    Unit = info.Unit,
                    Series = [],
                    Warning = ex.Message,
                });
            }
        }

        return new MetricsSeriesResponseDto
        {
            Status = MetricsStoreStatusCodes.Connected,
            Range = resolvedRange,
            Step = step,
            QueriedAtUtc = now,
            Panels = results,
        };
    }

    /// <summary>Instant KPI snapshot for the Metrics toolbar.</summary>
    public async Task<MetricsSummaryDto> GetSummaryAsync(
        string? range,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        string resolvedRange = MetricsRange.Normalize(range, _options.Metrics.DefaultRange);

        MetricsStatusDto status = await GetStatusAsync(probe: true, cancellationToken).ConfigureAwait(false);
        if (status.Status != MetricsStoreStatusCodes.Connected)
        {
            return new MetricsSummaryDto
            {
                Status = status.Status,
                Range = resolvedRange,
                QueriedAtUtc = now,
                Error = status.Error,
            };
        }

        try
        {
            double? requestRate = await InstantValueAsync("request_rate", cancellationToken).ConfigureAwait(false);
            double? ocHit = await InstantValueAsync("oc_hit_share", cancellationToken).ConfigureAwait(false);
            double? fcHit = await InstantValueAsync("fc_hit_rate", cancellationToken).ConfigureAwait(false);
            double? inv = await InstantValueAsync("invalidation_rate", cancellationToken).ConfigureAwait(false);

            return new MetricsSummaryDto
            {
                Status = MetricsStoreStatusCodes.Connected,
                Range = resolvedRange,
                QueriedAtUtc = now,
                RequestRate = requestRate,
                OcHitShare = ocHit,
                FcHitRate = fcHit,
                InvalidationRate = inv,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return new MetricsSummaryDto
            {
                Status = MetricsStoreStatusCodes.Disconnected,
                Range = resolvedRange,
                QueriedAtUtc = now,
                Error = ex.Message,
            };
        }
    }

    private async Task<double?> InstantValueAsync(string panelId, CancellationToken cancellationToken)
    {
        string promQl = MetricsPanelCatalog.BuildSummaryPromQl(panelId);
        IReadOnlyList<PrometheusInstantSample> samples = await _client
            .QueryInstantAsync(promQl, timeUtc: null, cancellationToken)
            .ConfigureAwait(false);
        return samples.FirstOrDefault()?.Value;
    }

    private static IReadOnlyList<string> ParsePanelList(string? panels)
    {
        if (string.IsNullOrWhiteSpace(panels))
        {
            return
            [
                "request_rate",
                "oc_hit_share",
                "fc_hit_rate",
                "invalidation_rate",
                "schedule_phase",
                "cluster_publish_failures",
                "fc_p95_ms",
            ];
        }

        List<string> list = [];
        foreach (string part in panels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (MetricsPanelCatalog.Find(part) is not null && !list.Contains(part, StringComparer.OrdinalIgnoreCase))
                list.Add(part);
        }

        return list.Count > 0 ? list : ParsePanelList(null);
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

    private static string SeriesName(string panelId, IReadOnlyDictionary<string, string> metric)
    {
        if (panelId is "schedule_phase" && metric.TryGetValue("phase", out string? phase) && phase.Length > 0)
            return phase;
        if (panelId is "cluster_publish_failures" && metric.TryGetValue("reason", out string? reason) && reason.Length > 0)
            return reason;
        if (metric.TryGetValue("domain", out string? domain) && domain.Length > 0)
            return domain;
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
