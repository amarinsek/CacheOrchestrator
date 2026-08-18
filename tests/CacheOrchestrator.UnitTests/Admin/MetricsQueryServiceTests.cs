using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services.Metrics;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.UnitTests.Admin;

public class MetricsQueryServiceTests
{
    [Fact]
    public async Task GetStatus_not_configured_when_disabled()
    {
        MetricsQueryService svc = CreateService(new MetricsStoreOptions { Enabled = false });
        MetricsStatusDto status = await svc.GetStatusAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MetricsStoreStatusCodes.NotConfigured, status.Status);
    }

    [Fact]
    public async Task GetStatus_not_configured_when_no_url()
    {
        MetricsQueryService svc = CreateService(new MetricsStoreOptions
        {
            Enabled = true,
            BaseUrl = "  ",
        });
        MetricsStatusDto status = await svc.GetStatusAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MetricsStoreStatusCodes.NotConfigured, status.Status);
    }

    [Fact]
    public async Task GetSeries_returns_empty_when_not_configured()
    {
        MetricsQueryService svc = CreateService(new MetricsStoreOptions { Enabled = false });
        MetricsSeriesResponseDto series = await svc.GetSeriesAsync(
            "1h", null, null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MetricsStoreStatusCodes.NotConfigured, series.Status);
        Assert.Empty(series.Panels);
    }

    [Fact]
    public async Task GetStatus_connected_when_probe_ok()
    {
        FakeMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 12 },
        };
        MetricsQueryService svc = CreateService(
            new MetricsStoreOptions
            {
                Enabled = true,
                Provider = "Prometheus",
                BaseUrl = "http://prometheus:9090",
            },
            client);

        MetricsStatusDto status = await svc.GetStatusAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MetricsStoreStatusCodes.Connected, status.Status);
        Assert.Equal("prometheus:9090", status.Host);
        Assert.Equal(12, status.LatencyMs);
    }

    [Fact]
    public async Task GetSeries_maps_matrix_to_panels()
    {
        FakeMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 1 },
            Matrix =
            [
                new PrometheusMatrixSeries
                {
                    Metric = new Dictionary<string, string> { ["domain"] = "catalog" },
                    Points =
                    [
                        new MetricsPointDto { T = 100, V = 0.5 },
                        new MetricsPointDto { T = 130, V = 0.7 },
                    ],
                },
            ],
        };

        MetricsQueryService svc = CreateService(
            new MetricsStoreOptions
            {
                Enabled = true,
                BaseUrl = "http://localhost:9090",
            },
            client);

        MetricsSeriesResponseDto series = await svc.GetSeriesAsync(
            "1h", "request_rate", null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MetricsStoreStatusCodes.Connected, series.Status);
        Assert.Single(series.Panels);
        Assert.Equal("request_rate", series.Panels[0].Id);
        Assert.Equal("catalog", series.Panels[0].Series[0].Name);
        Assert.Equal(2, series.Panels[0].Series[0].Points.Count);
        Assert.Contains("rate(", client.LastPromQl ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSummary_not_configured()
    {
        MetricsQueryService svc = CreateService(new MetricsStoreOptions { Enabled = false });
        MetricsSummaryDto summary = await svc.GetSummaryAsync(
            "1h", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MetricsStoreStatusCodes.NotConfigured, summary.Status);
        Assert.Null(summary.RequestRate);
    }

    [Fact]
    public async Task GetSummary_maps_instant_values()
    {
        FakeMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 1 },
            InstantHandler = _ =>
            [
                new PrometheusInstantSample
                {
                    Metric = new Dictionary<string, string>(),
                    Value = 3.5,
                },
            ],
        };
        MetricsQueryService svc = CreateService(
            new MetricsStoreOptions { Enabled = true, BaseUrl = "http://localhost:9090" },
            client);

        MetricsSummaryDto summary = await svc.GetSummaryAsync(
            "15m", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MetricsStoreStatusCodes.Connected, summary.Status);
        Assert.Equal(3.5, summary.RequestRate);
        Assert.False(summary.NoData);
    }

    [Fact]
    public async Task GetSeries_absolute_range_sets_from_to()
    {
        FakeMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 1 },
            Matrix = [],
        };
        MetricsQueryService svc = CreateService(
            new MetricsStoreOptions { Enabled = true, BaseUrl = "http://localhost:9090" },
            client);

        string from = "2026-01-01T00:00:00Z";
        string to = "2026-01-01T01:00:00Z";
        MetricsSeriesResponseDto series = await svc.GetSeriesAsync(
            range: null,
            panels: "request_rate",
            domains: null,
            instances: null,
            routes: null,
            from: from,
            to: to,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(MetricsStoreStatusCodes.Connected, series.Status);
        Assert.Equal("custom", series.Range);
        Assert.Equal(DateTimeOffset.Parse(from), series.FromUtc);
        Assert.Equal(DateTimeOffset.Parse(to), series.ToUtc);
        Assert.NotNull(client.LastRangeStart);
        Assert.NotNull(client.LastRangeEnd);
    }

    [Fact]
    public async Task GetSeries_unknown_panel_only_FallsBackToDefaultPanels()
    {
        FakeMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 1 },
        };
        MetricsQueryService svc = CreateService(
            new MetricsStoreOptions { Enabled = true, BaseUrl = "http://localhost:9090" },
            client);

        // ParsePanelList drops unknown ids and falls back to the default Metrics page set.
        MetricsSeriesResponseDto series = await svc.GetSeriesAsync(
            "1h", "not_a_real_panel", null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MetricsStoreStatusCodes.Connected, series.Status);
        Assert.Equal(MetricsPanelCatalog.DefaultPagePanels.Count, series.Panels.Count);
        Assert.DoesNotContain(series.Panels, p => p.Id == "not_a_real_panel");
    }

    [Fact]
    public async Task GetSeries_query_failure_sets_panel_warning()
    {
        FakeMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 1 },
            RangeThrows = new InvalidOperationException("prom boom"),
        };
        MetricsQueryService svc = CreateService(
            new MetricsStoreOptions { Enabled = true, BaseUrl = "http://localhost:9090" },
            client);

        MetricsSeriesResponseDto series = await svc.GetSeriesAsync(
            "1h", "request_rate", null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(series.Panels);
        Assert.Equal("prom boom", series.Panels[0].Warning);
        Assert.Empty(series.Panels[0].Series);
    }

    private static MetricsQueryService CreateService(
        MetricsStoreOptions metrics,
        IMetricsQueryClient? client = null)
    {
        AdminConsoleOptions opts = new() { Metrics = metrics };
        return new MetricsQueryService(
            client ?? new FakeMetricsClient(),
            Options.Create(opts),
            TimeProvider.System);
    }

    private sealed class FakeMetricsClient : IMetricsQueryClient
    {
        public MetricsProbeResult Probe { get; set; } = new() { Succeeded = false, Error = "n/a" };
        public IReadOnlyList<PrometheusMatrixSeries> Matrix { get; set; } = [];
        public Func<string, IReadOnlyList<PrometheusInstantSample>> InstantHandler { get; set; } = _ => [];
        public Exception? RangeThrows { get; set; }
        public string? LastPromQl { get; private set; }
        public DateTimeOffset? LastRangeStart { get; private set; }
        public DateTimeOffset? LastRangeEnd { get; private set; }

        public Task<MetricsProbeResult> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Probe);

        public Task<IReadOnlyList<PrometheusMatrixSeries>> QueryRangeAsync(
            string promQl,
            DateTimeOffset startUtc,
            DateTimeOffset endUtc,
            string step,
            CancellationToken cancellationToken = default)
        {
            LastPromQl = promQl;
            LastRangeStart = startUtc;
            LastRangeEnd = endUtc;
            if (RangeThrows is not null)
                throw RangeThrows;
            return Task.FromResult(Matrix);
        }

        public Task<IReadOnlyList<PrometheusInstantSample>> QueryInstantAsync(
            string promQl,
            DateTimeOffset? timeUtc = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InstantHandler(promQl));
    }
}
