using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services;
using CacheOrchestrator.AdminConsole.Services.Hints;
using CacheOrchestrator.AdminConsole.Services.Metrics;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.UnitTests.Admin;

public class LiveStatsServiceTests
{
    [Fact]
    public async Task GetAsync_NotConfigured_ReturnsHealthWithoutRates()
    {
        LiveStatsService sut = CreateSut(
            new MetricsStoreOptions { Enabled = false },
            new ScriptedMetricsClient());

        LiveSnapshotDto snap = await sut.GetAsync(TestContext.Current.CancellationToken);

        snap.Status.Should().Be(MetricsStoreStatusCodes.NotConfigured);
        snap.Instances.Should().ContainSingle(i => i.Id == "app-1");
        snap.Domains.Should().BeEmpty();
        snap.Endpoints.Should().BeEmpty();
        snap.HintSummary.Critical.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_Connected_MapsClusterAndEntityRates()
    {
        ScriptedMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 1 },
            InstantHandler = promQl =>
            {
                // Cluster OC rate (no "by")
                if (promQl.Contains($"sum(rate({MetricsPanelCatalog.OcRequests}[1m]))", StringComparison.Ordinal)
                    && !promQl.Contains("by (", StringComparison.Ordinal)
                    && !promQl.Contains("result=", StringComparison.Ordinal))
                {
                    return [Sample(12.5)];
                }

                if (promQl.Contains("sum by (domain)", StringComparison.Ordinal)
                    && promQl.Contains(MetricsPanelCatalog.OcRequests, StringComparison.Ordinal)
                    && !promQl.Contains("result=", StringComparison.Ordinal))
                {
                    return [Sample(12.5, ("domain", "catalog"))];
                }

                if (promQl.Contains("sum by (route,domain)", StringComparison.Ordinal)
                    && promQl.Contains(MetricsPanelCatalog.OcRequests, StringComparison.Ordinal)
                    && !promQl.Contains("result=", StringComparison.Ordinal))
                {
                    return [Sample(12.5, ("route", "GET /api/catalog"), ("domain", "catalog"))];
                }

                if (promQl.Contains("sum by (instance_id)", StringComparison.Ordinal))
                {
                    return [Sample(12.5, ("instance_id", "app-1"))];
                }

                return [];
            },
        };

        LiveStatsService sut = CreateSut(
            new MetricsStoreOptions
            {
                Enabled = true,
                BaseUrl = "http://localhost:9090",
            },
            client,
            domains:
            [
                new AdminDomainConfigDto
                {
                    Name = "catalog",
                    Version = "1",
                    FusionCacheInstanceName = "default",
                },
                new AdminDomainConfigDto
                {
                    Name = "quiet",
                    Version = "1",
                    FusionCacheInstanceName = "default",
                },
            ]);

        LiveSnapshotDto snap = await sut.GetAsync(TestContext.Current.CancellationToken);

        snap.Status.Should().Be(MetricsStoreStatusCodes.Connected);
        snap.Lookback.Should().Be(LiveStatsService.DefaultLookback);
        snap.Cluster.RequestRate.Should().BeApproximately(12.5, 0.001);
        snap.Domains.Should().ContainSingle(d => d.Name == "catalog" && d.RequestRate == 12.5);
        snap.Endpoints.Should().ContainSingle(e => e.Name == "GET /api/catalog");
        snap.QuietDomains.Should().Contain("quiet");
        // Lightweight hints from live rates (may be empty when shares are healthy).
        snap.HintSummary.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAsync_HighFactoryShare_EmitsHintWarningOrCritical()
    {
        ScriptedMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 1 },
            InstantHandler = promQl =>
            {
                if (promQl.Contains($"sum(rate({MetricsPanelCatalog.OcRequests}[1m]))", StringComparison.Ordinal)
                    && !promQl.Contains("by (", StringComparison.Ordinal)
                    && !promQl.Contains("result=", StringComparison.Ordinal))
                {
                    return [Sample(10)];
                }

                if (promQl.Contains("sum by (domain)", StringComparison.Ordinal)
                    && promQl.Contains(MetricsPanelCatalog.OcRequests, StringComparison.Ordinal)
                    && !promQl.Contains("result=", StringComparison.Ordinal))
                {
                    return [Sample(10, ("domain", "hot"))];
                }

                if (promQl.Contains("sum by (domain)", StringComparison.Ordinal)
                    && promQl.Contains(MetricsPanelCatalog.FcRequests, StringComparison.Ordinal)
                    && promQl.Contains("result=\"miss\"", StringComparison.Ordinal))
                {
                    // Factory rate ≈ OC rate → factory share ~1.0
                    return [Sample(10, ("domain", "hot"))];
                }

                return [];
            },
        };

        LiveStatsService sut = CreateSut(
            new MetricsStoreOptions { Enabled = true, BaseUrl = "http://localhost:9090" },
            client,
            domains:
            [
                new AdminDomainConfigDto
                {
                    Name = "hot",
                    Version = "1",
                    FusionCacheInstanceName = "default",
                },
            ]);

        LiveSnapshotDto snap = await sut.GetAsync(TestContext.Current.CancellationToken);
        snap.Status.Should().Be(MetricsStoreStatusCodes.Connected);
        snap.Domains.Should().ContainSingle(d => d.Name == "hot");
        (snap.HintSummary.Warning + snap.HintSummary.Critical).Should().BeGreaterThan(0);
    }

    private static LiveStatsService CreateSut(
        MetricsStoreOptions metrics,
        ScriptedMetricsClient client,
        IReadOnlyList<AdminDomainConfigDto>? domains = null)
    {
        AdminConsoleOptions opts = new()
        {
            Metrics = metrics,
            Instances = [new AdminInstanceOptions { Id = "app-1", Url = "http://app-1" }],
        };
        IOptions<AdminConsoleOptions> options = Options.Create(opts);
        MetricsQueryService metricsSvc = new(client, options, TimeProvider.System);
        FakeLocal local = new(domains ?? []);
        InstanceReachabilityCache reachability = new(options, TimeProvider.System);
        AdminFanOutService fanOut = new(local, options, reachability, TimeProvider.System);
        HintEngine hints = TestHintEngine.Create(opts);
        return new LiveStatsService(client, metricsSvc, fanOut, hints, TimeProvider.System);
    }

    private static PrometheusInstantSample Sample(double value, params (string Key, string Value)[] labels) =>
        new()
        {
            Metric = labels.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
            Value = value,
        };

    private sealed class ScriptedMetricsClient : IMetricsQueryClient
    {
        public MetricsProbeResult Probe { get; set; } = new() { Succeeded = false, Error = "n/a" };
        public Func<string, IReadOnlyList<PrometheusInstantSample>> InstantHandler { get; set; } = _ => [];

        public Task<MetricsProbeResult> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Probe);

        public Task<IReadOnlyList<PrometheusMatrixSeries>> QueryRangeAsync(
            string promQl,
            DateTimeOffset startUtc,
            DateTimeOffset endUtc,
            string step,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrometheusMatrixSeries>>([]);

        public Task<IReadOnlyList<PrometheusInstantSample>> QueryInstantAsync(
            string promQl,
            DateTimeOffset? timeUtc = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InstantHandler(promQl));
    }

    private sealed class FakeLocal(IReadOnlyList<AdminDomainConfigDto> domains) : ILocalAdminClient
    {
        public Task<InstanceCallOutcome<AdminHealthDto>> GetHealthAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstanceCallOutcome<AdminHealthDto>
            {
                InstanceId = instance.Id,
                Succeeded = true,
                StatusCode = 200,
                LatencyMs = 1,
                Value = new AdminHealthDto
                {
                    Healthy = true,
                    InstanceId = instance.Id,
                    UtcNow = DateTimeOffset.UtcNow,
                    AdminEnabled = true,
                },
            });

        public Task<InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>> GetDomainsAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>
            {
                InstanceId = instance.Id,
                Succeeded = true,
                StatusCode = 200,
                LatencyMs = 1,
                Value = domains,
            });

        public Task<InstanceCallOutcome<CacheOrchestrator.Invalidation.CacheInvalidationResult>> InvalidateAsync(
            AdminInstanceOptions instance,
            AdminInvalidateRequest body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> SetVersionAsync(
            AdminInstanceOptions instance,
            string domain,
            AdminVersionRequest body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> PatchTtlAsync(
            AdminInstanceOptions instance,
            string domain,
            AdminTtlPatchRequest body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstanceCallOutcome<LocalClusterInfoDto>> GetClusterInfoAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstanceCallOutcome<LocalClusterInfoDto>
            {
                InstanceId = instance.Id,
                Succeeded = false,
                Error = "no bus",
                LatencyMs = 1,
            });
    }
}
