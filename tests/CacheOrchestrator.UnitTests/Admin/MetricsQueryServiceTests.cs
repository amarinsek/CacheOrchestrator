using CacheOrchestrator.Admin.App.Models;
using CacheOrchestrator.Admin.App.Options;
using CacheOrchestrator.Admin.App.Services.Metrics;
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
            "1h", null, null, TestContext.Current.CancellationToken);
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
            "1h", "request_rate", null, TestContext.Current.CancellationToken);
        Assert.Equal(MetricsStoreStatusCodes.Connected, series.Status);
        Assert.Single(series.Panels);
        Assert.Equal("request_rate", series.Panels[0].Id);
        Assert.Equal("catalog", series.Panels[0].Series[0].Name);
        Assert.Equal(2, series.Panels[0].Series[0].Points.Count);
        Assert.Contains("rate(", client.LastPromQl ?? "", StringComparison.Ordinal);
    }

    private static MetricsQueryService CreateService(
        MetricsStoreOptions metrics,
        IMetricsQueryClient? client = null)
    {
        CacheAdminOptions opts = new() { Metrics = metrics };
        return new MetricsQueryService(
            client ?? new FakeMetricsClient(),
            Options.Create(opts),
            TimeProvider.System);
    }

    private sealed class FakeMetricsClient : IMetricsQueryClient
    {
        public MetricsProbeResult Probe { get; set; } = new() { Succeeded = false, Error = "n/a" };
        public IReadOnlyList<PrometheusMatrixSeries> Matrix { get; set; } = [];
        public string? LastPromQl { get; private set; }

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
            return Task.FromResult(Matrix);
        }

        public Task<IReadOnlyList<PrometheusInstantSample>> QueryInstantAsync(
            string promQl,
            DateTimeOffset? timeUtc = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrometheusInstantSample>>([]);
    }
}
