using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services;
using CacheOrchestrator.AdminConsole.Services.Hints;
using CacheOrchestrator.AdminConsole.Services.Metrics;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class MetricsWindowStatsServiceTests
{
    [Fact]
    public async Task GetAsync_NotConfigured_ReturnsEnvelope()
    {
        MetricsWindowStatsService sut = CreateSut(
            metrics: new MetricsStoreOptions { Enabled = false },
            client: new ScriptedMetricsClient());

        WindowStatsDto result = await sut.GetAsync(
            "1h", null, null, cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(MetricsStoreStatusCodes.NotConfigured);
        result.Domains.Should().BeEmpty();
        result.Endpoints.Should().BeEmpty();
        result.NoData.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_Connected_MergesDomainAndEndpointWindowCounts()
    {
        ScriptedMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 1 },
            InstantHandler = promQl =>
            {
                if (promQl.Contains("max_over_time", StringComparison.Ordinal))
                    return [];

                // Domain tables roll up from per-instance series.
                if (promQl.Contains(MetricsPanelCatalog.OcRequests, StringComparison.Ordinal)
                    && promQl.Contains("sum by (domain,result,instance_id)", StringComparison.Ordinal))
                {
                    return
                    [
                        Sample(40, ("domain", "catalog"), ("result", "hit"), ("instance_id", "app-1")),
                        Sample(10, ("domain", "catalog"), ("result", "miss"), ("instance_id", "app-1")),
                    ];
                }

                if (promQl.Contains(MetricsPanelCatalog.DcRequests, StringComparison.Ordinal)
                    && promQl.Contains("sum by (domain,result,instance_id)", StringComparison.Ordinal))
                {
                    return
                    [
                        Sample(8, ("domain", "catalog"), ("result", "hit"), ("instance_id", "app-1")),
                        Sample(2, ("domain", "catalog"), ("result", "miss"), ("instance_id", "app-1")),
                    ];
                }

                if (promQl.Contains(MetricsPanelCatalog.OcRequests, StringComparison.Ordinal)
                    && promQl.Contains("sum by (route,result,domain)", StringComparison.Ordinal))
                {
                    return
                    [
                        Sample(40, ("route", "GET /api/catalog"), ("domain", "catalog"), ("result", "hit")),
                        Sample(10, ("route", "GET /api/catalog"), ("domain", "catalog"), ("result", "miss")),
                    ];
                }

                if (promQl.Contains(MetricsPanelCatalog.DcRequests, StringComparison.Ordinal)
                    && promQl.Contains("sum by (route,result,domain)", StringComparison.Ordinal))
                {
                    return
                    [
                        Sample(8, ("route", "GET /api/catalog"), ("domain", "catalog"), ("result", "hit")),
                        Sample(2, ("route", "GET /api/catalog"), ("domain", "catalog"), ("result", "miss")),
                    ];
                }

                if (promQl.Contains(MetricsPanelCatalog.FactoryRuns, StringComparison.Ordinal)
                    && promQl.Contains("sum by (domain,instance_id)", StringComparison.Ordinal))
                {
                    return [Sample(2, ("domain", "catalog"), ("instance_id", "app-1"))];
                }

                if (promQl.Contains(MetricsPanelCatalog.FactoryRuns, StringComparison.Ordinal)
                    && promQl.Contains("sum by (route,domain)", StringComparison.Ordinal))
                {
                    return [Sample(2, ("route", "GET /api/catalog"), ("domain", "catalog"))];
                }

                if (promQl.Contains(MetricsPanelCatalog.FactoryRuns, StringComparison.Ordinal)
                    && promQl.Contains("sum by (route,instance_id)", StringComparison.Ordinal))
                {
                    return [Sample(2, ("route", "GET /api/catalog"), ("instance_id", "app-1"))];
                }

                // Invalidations / factory / by-instance extras → empty
                return [];
            },
        };

        MetricsWindowStatsService sut = CreateSut(
            metrics: new MetricsStoreOptions
            {
                Enabled = true,
                Provider = "Prometheus",
                BaseUrl = "http://prometheus:9090",
            },
            client,
            domains:
            [
                new AdminDomainConfigDto
                {
                    Name = "catalog",
                    Version = "v1",
                    DataCacheInstanceName = "default",
                },
            ]);

        WindowStatsDto result = await sut.GetAsync(
            "1h", null, null, cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(MetricsStoreStatusCodes.Connected);
        result.NoData.Should().BeFalse();
        result.TotalRequests.Should().Be(50);
        result.Domains.Should().ContainSingle(d => d.Name == "catalog" && d.Requests == 50);
        result.Domains[0].DataCache.FactoryRuns.Should().Be(2);
        result.Endpoints.Should().ContainSingle(e =>
            e.Route == "GET /api/catalog" && e.ConfiguredDomain == "catalog" && e.Requests == 50);
        result.Domains[0].Version.Should().Be("v1");
    }

    [Fact]
    public async Task GetAsync_ZeroTrafficSamples_AreDroppedFromTables()
    {
        ScriptedMetricsClient client = new()
        {
            Probe = new MetricsProbeResult { Succeeded = true, LatencyMs = 1 },
            InstantHandler = promQl =>
            {
                if (promQl.Contains(MetricsPanelCatalog.OcRequests, StringComparison.Ordinal)
                    && promQl.Contains("sum by (domain,result,instance_id)", StringComparison.Ordinal))
                {
                    // Value 0 must not create a table row.
                    return [Sample(0, ("domain", "idle"), ("result", "hit"), ("instance_id", "app-1"))];
                }

                return [];
            },
        };

        MetricsWindowStatsService sut = CreateSut(
            metrics: new MetricsStoreOptions
            {
                Enabled = true,
                BaseUrl = "http://localhost:9090",
            },
            client);

        WindowStatsDto result = await sut.GetAsync(
            "15m", null, null, cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(MetricsStoreStatusCodes.Connected);
        result.Domains.Should().BeEmpty();
        result.NoData.Should().BeTrue();
    }

    private static MetricsWindowStatsService CreateSut(
        MetricsStoreOptions metrics,
        ScriptedMetricsClient client,
        IReadOnlyList<AdminDomainConfigDto>? domains = null)
    {
        AdminConsoleOptions opts = new()
        {
            Metrics = metrics,
            Instances =
            [
                new AdminInstanceOptions { Id = "app-1", Url = "http://app-1" },
            ],
        };
        IOptions<AdminConsoleOptions> options = Microsoft.Extensions.Options.Options.Create(opts);
        MetricsQueryService status = new(client, options, TimeProvider.System);
        HintEngine hints = TestHintEngine.Create(opts);
        FakeFanOutLocalClient local = new(domains ?? []);
        InstanceReachabilityCache reachability = new(options, TimeProvider.System);
        AdminFanOutService fanOut = new(local, options, reachability, TimeProvider.System);
        return new MetricsWindowStatsService(client, status, hints, fanOut, TimeProvider.System);
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

    private sealed class FakeFanOutLocalClient(IReadOnlyList<AdminDomainConfigDto> domains) : IAdminApiClient
    {
        public Task<InstanceCallOutcome<AdminHealthDto>> GetHealthAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Ok(instance.Id, new AdminHealthDto
            {
                Healthy = true,
                InstanceId = instance.Id,
                UtcNow = DateTimeOffset.UtcNow,
                AdminEnabled = true,
            }));

        public Task<InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>> GetDomainsAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Ok(instance.Id, domains));

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

        public Task<InstanceCallOutcome<AdminDomainMutationResultDto>> PatchSettingsAsync(
            AdminInstanceOptions instance,
            string domain,
            AdminSettingsPatchRequest body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstanceCallOutcome<AdminDomainSettingsCatalogDto>> GetDomainSettingsCatalogAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InstanceCallOutcome<AdminApiClusterInfoDto>> GetClusterInfoAsync(
            AdminInstanceOptions instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Fail<AdminApiClusterInfoDto>(instance.Id, "no bus"));

        private static InstanceCallOutcome<T> Ok<T>(string id, T value) =>
            new()
            {
                InstanceId = id,
                Succeeded = true,
                Value = value,
                StatusCode = 200,
                LatencyMs = 1,
            };

        private static InstanceCallOutcome<T> Fail<T>(string id, string error) =>
            new()
            {
                InstanceId = id,
                Succeeded = false,
                Error = error,
                LatencyMs = 1,
            };
    }
}
