using CacheOrchestrator.AdminConsole.Services;

namespace CacheOrchestrator.UnitTests.Admin;

public class StatsDeltaCacheTests
{
    [Fact]
    public void FirstSample_HasNoDelta()
    {
        StatsDeltaCache cache = new();
        (var impact, string? label) = cache.RecordAndDiff("k", 100, 10, 100, 10);
        impact.Should().BeNull();
        label.Should().BeNull();
    }

    [Fact]
    public void SecondSample_ComputesDeltaImpact()
    {
        StatsDeltaCache cache = new();
        cache.RecordAndDiff("k", 100, 10, 100, 10);
        (var impact, string? label) = cache.RecordAndDiff("k", 200, 20, 300, 20);

        impact.Should().NotBeNull();
        impact!.FactoryShare.Should().BeApproximately(0.1, 0.001);
        impact.FactoryAvoidance.Should().BeApproximately(0.9, 0.001);
        impact.AvgFactoryDurationMs.Should().BeApproximately(20, 0.01); // (300-100)/(20-10)
        label.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CounterReset_SkipsNegativeDelta()
    {
        StatsDeltaCache cache = new();
        cache.RecordAndDiff("k", 1000, 100, 500, 50);
        (var impact, _) = cache.RecordAndDiff("k", 10, 1, 5, 1);
        impact.Should().BeNull();
    }
}
