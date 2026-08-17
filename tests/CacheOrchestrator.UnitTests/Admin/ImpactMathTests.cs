using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Services;

namespace CacheOrchestrator.UnitTests.Admin;

public class ImpactMathTests
{
    [Fact]
    public void LowTrafficCheapFactory_IsPoorCandidate()
    {
        // 1 request, no factory runs, one historical duration sample of 1 ms
        CacheImpactKpiDto kpi = ImpactMath.Compute(
            requests: 1,
            factoryRuns: 0,
            factoryDurationSumMs: 1,
            factoryDurationCount: 1);

        kpi.LowRequestSample.Should().BeTrue();
        kpi.Candidate.Should().Be("INSUFFICIENT_DATA");
        kpi.Benefit.Should().Be("UNKNOWN");
    }

    [Fact]
    public void EnoughRequests_LowCost_HighAvoidance_IsLowGainOrLimited()
    {
        CacheImpactKpiDto kpi = ImpactMath.Compute(
            requests: 100,
            factoryRuns: 5,
            factoryDurationSumMs: 5, // 1 ms avg
            factoryDurationCount: 5);

        kpi.FactoryAvoidance.Should().BeApproximately(0.95, 0.001);
        kpi.AvgFactoryDurationMs.Should().BeApproximately(1.0, 0.01);
        kpi.Benefit.Should().Be("LOW_GAIN");
        kpi.Candidate.Should().Be("LIMITED");
    }

    [Fact]
    public void HighTraffic_ExpensiveFactory_HighAvoidance_IsStrong()
    {
        CacheImpactKpiDto kpi = ImpactMath.Compute(
            requests: 10_000,
            factoryRuns: 500,
            factoryDurationSumMs: 500 * 80,
            factoryDurationCount: 500);

        kpi.FactoryAvoidance.Should().BeApproximately(0.95, 0.001);
        kpi.EstFactoryTimeSavedMs.Should().BeApproximately(9500 * 80, 1);
        kpi.Benefit.Should().Be("HIGH");
        kpi.Candidate.Should().Be("STRONG");
    }

    [Fact]
    public void HighFactoryShare_NeedsTuning()
    {
        CacheImpactKpiDto kpi = ImpactMath.Compute(
            requests: 1000,
            factoryRuns: 600,
            factoryDurationSumMs: 600 * 40,
            factoryDurationCount: 600);

        kpi.Candidate.Should().Be("NEEDS_TUNING");
        kpi.Benefit.Should().Be("AT_RISK");
    }

    [Fact]
    public void LargeResultSize_RaisesCost_AndPayloadOffload()
    {
        CacheImpactKpiDto kpi = ImpactMath.Compute(
            requests: 10_000,
            factoryRuns: 100,
            factoryDurationSumMs: 100 * 1, // 1 ms — duration LOW
            factoryDurationCount: 100,
            factoryResultSizeSumBytes: 100L * 200_000, // 200 KB avg — size HIGH
            factoryResultSizeCount: 100);

        kpi.AvgFactoryResultSizeBytes.Should().BeApproximately(200_000, 1);
        kpi.EstPayloadOffloadBytes.Should().BeApproximately(9900 * 200_000, 1);
        kpi.Benefit.Should().Be("HIGH"); // high avoidance + high size cost
        kpi.Candidate.Should().Be("STRONG");
    }

    [Fact]
    public void WithEstTimeSaved_OverridesClusterEstimate_KeepsOtherFields()
    {
        CacheImpactKpiDto kpi = ImpactMath.Compute(
            requests: 10_000,
            factoryRuns: 500,
            factoryDurationSumMs: 500 * 80,
            factoryDurationCount: 500);

        CacheImpactKpiDto summed = ImpactMath.WithEstTimeSaved(kpi, 1_800 + 530 + 5_100);
        summed.EstFactoryTimeSavedMs.Should().Be(7_430);
        summed.AvgFactoryDurationMs.Should().Be(kpi.AvgFactoryDurationMs);
        summed.Benefit.Should().Be(kpi.Benefit);
    }
}
