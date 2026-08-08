using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.UnitTests.Health;

public class CacheOrchestratorHealthCheckTests
{
    private static (CacheOrchestratorHealthCheck Sut, HealthCheckContext Ctx) Create(
        IEnumerable<ICacheOrchestratorHealthProbe> probes,
        HealthStatus failureStatus = HealthStatus.Degraded)
    {
        var monitor = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        monitor.CurrentValue.Returns(new CacheOrchestratorOptions());

        var sut = new CacheOrchestratorHealthCheck(monitor, probes);
        var ctx = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                name: "cache_orchestrator",
                factory: _ => sut,
                failureStatus: failureStatus,
                tags: null,
                timeout: TimeSpan.FromSeconds(3))
        };
        return (sut, ctx);
    }

    [Fact]
    public async Task CheckHealth_WhenNoProbes_ReturnsHealthy()
    {
        var (sut, ctx) = Create([]);

        var result = await sut.CheckHealthAsync(ctx, TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_WhenAllProbesSucceed_ReturnsHealthy()
    {
        var probe = Substitute.For<ICacheOrchestratorHealthProbe>();
        probe.Name.Returns("inmemory");
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var (sut, ctx) = Create([probe]);

        var result = await sut.CheckHealthAsync(ctx, TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["probe:inmemory"].Should().Be("ok");
    }

    [Fact]
    public async Task CheckHealth_WhenProbeFails_ReturnsConfiguredFailureStatus()
    {
        var probe = Substitute.For<ICacheOrchestratorHealthProbe>();
        probe.Name.Returns("redis");
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Redis down")));

        var (sut, ctx) = Create([probe], failureStatus: HealthStatus.Degraded);

        var result = await sut.CheckHealthAsync(ctx, TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("redis");
        result.Data["probe:redis"].Should().Be("fail");
    }
}