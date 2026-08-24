using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AspNetCore.UnitTests.Admin;

public class AdminQueryServiceTests
{
    [Fact]
    public async Task GetHealthAsync_WhenNoProbes_IsHealthy()
    {
        AdminQueryService sut = CreateSut();

        AdminHealthDto health = await sut.GetHealthAsync(TestContext.Current.CancellationToken);

        health.Healthy.Should().BeTrue();
        health.AdminEnabled.Should().BeTrue();
        health.InstanceId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetHealthAsync_WhenProbeFails_IsDegraded()
    {
        ICacheOrchestratorHealthProbe probe = Substitute.For<ICacheOrchestratorHealthProbe>();
        probe.Name.Returns("redis");
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("redis down")));

        AdminQueryService sut = CreateSut(probes: [probe]);

        AdminHealthDto health = await sut.GetHealthAsync(TestContext.Current.CancellationToken);

        health.Healthy.Should().BeFalse();
        health.InstanceId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetHealthAsync_WhenStatsThrow_IsDegraded()
    {
        IAdminStatsCollector stats = Substitute.For<IAdminStatsCollector>();
        stats.GetRawSnapshot().Returns(_ => throw new InvalidOperationException("counters"));

        AdminQueryService sut = CreateSut(stats: stats);

        AdminHealthDto health = await sut.GetHealthAsync(TestContext.Current.CancellationToken);

        health.Healthy.Should().BeFalse();
    }

    [Fact]
    public async Task GetHealthAsync_WhenProbeSucceeds_IsHealthy()
    {
        ICacheOrchestratorHealthProbe probe = Substitute.For<ICacheOrchestratorHealthProbe>();
        probe.Name.Returns("redis");
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        AdminQueryService sut = CreateSut(probes: [probe]);

        AdminHealthDto health = await sut.GetHealthAsync(TestContext.Current.CancellationToken);

        health.Healthy.Should().BeTrue();
        await probe.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
    }

    private static AdminQueryService CreateSut(
        IAdminStatsCollector? stats = null,
        IEnumerable<ICacheOrchestratorHealthProbe>? probes = null)
    {
        if (stats is null)
        {
            stats = Substitute.For<IAdminStatsCollector>();
            stats.GetRawSnapshot().Returns(new AdminLiveStatsRawSnapshot
            {
                InstanceId = "unit",
                CollectedAtUtc = DateTimeOffset.UtcNow,
                Domains = [],
                UnassignedEndpoints = []
            });
        }

        IOptionsMonitor<CacheOrchestratorOptions> options = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            InstanceId = "unit",
            Admin = { Enabled = true }
        });

        return new AdminQueryService(
            stats,
            Substitute.For<IAdminEndpointCatalog>(),
            Substitute.For<IDomainCacheOptionsProvider>(),
            Substitute.For<IDomainRuntimeOverrideStore>(),
            options,
            TimeProvider.System,
            probes);
    }
}
