using CacheOrchestrator.Diagnostics;
using System.Diagnostics;

namespace CacheOrchestrator.Core.UnitTests.Diagnostics;

public class CacheOrchestratorActivitySourceTests
{
    [Fact]
    public void Source_Name_IsStable()
    {
        CacheOrchestratorActivitySource.Name.Should().Be("CacheOrchestrator");
        CacheOrchestratorActivitySource.Source.Name.Should().Be("CacheOrchestrator");
    }

    [Fact]
    public void StartActivity_WhenListenerPresent_CreatesActivity_WithExpectedName()
    {
        Activity? started = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CacheOrchestratorActivitySource.Name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => started = activity
        };

        ActivitySource.AddActivityListener(listener);

        using var activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.dc.get_or_set");

        activity.Should().NotBeNull();
        activity.OperationName.Should().Be("cache.dc.get_or_set");
        started.Should().NotBeNull();
        started.OperationName.Should().Be("cache.dc.get_or_set");
    }

    [Fact]
    public void StartActivity_CanSetDomainAndResultTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CacheOrchestratorActivitySource.Name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.dc.get_or_set");
        activity.Should().NotBeNull();

        activity.SetTag("domain", "catalog");
        activity.SetTag("cache.result", "hit");

        var tags = activity.Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("domain").WhoseValue.Should().Be("catalog");
        tags.Should().ContainKey("cache.result").WhoseValue.Should().Be("hit");
    }

    [Fact]
    public void StartActivity_Invalidate_Name()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CacheOrchestratorActivitySource.Name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.invalidate");

        activity.Should().NotBeNull();
        activity.OperationName.Should().Be("cache.invalidate");
    }

    [Fact]
    public void StartActivity_OutputHit_Name()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CacheOrchestratorActivitySource.Name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.oc.hit");

        activity.Should().NotBeNull();
        activity.OperationName.Should().Be("cache.oc.hit");
    }
}