using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using System.Diagnostics;

namespace CacheOrchestrator.Core.UnitTests.Admin;

public class InMemoryAdminStatsCollectorTests
{
    [Fact]
    public void DirectFactory_ContributesFactoryStatsWithoutDataCacheOutcome()
    {
        InMemoryAdminStatsCollector collector = new(
            new CacheOrchestratorOptions.AdminOptions
            {
                Enabled = true,
                TrackEndpoints = true,
                TrackLatency = true
            },
            instanceId: "direct");

        collector.RecordOutput("GET /promotions", "promotions", "miss");
        collector.RecordFactory(
            "GET /promotions",
            "promotions",
            failed: false,
            elapsedTicks: Stopwatch.Frequency / 100);

        AdminDomainCountersDto domain = collector.GetRawSnapshot().Domains.Single();
        domain.DataCacheHits.Should().Be(0);
        domain.DataCacheMisses.Should().Be(0);
        domain.FactoryRuns.Should().Be(1);
        domain.FactoryFailures.Should().Be(0);
        domain.FactoryDurationCount.Should().Be(1);
    }

    [Fact]
    public void GetRawSnapshot_ExposesFlatCounters_WithoutDerivedShares()
    {
        InMemoryAdminStatsCollector collector = new(
            new CacheOrchestratorOptions.AdminOptions
            {
                Enabled = true,
                TrackEndpoints = true
            },
            instanceId: "raw-1");

        collector.RecordOutput("GET /api/products/{id}", "catalog", "hit");
        collector.RecordOutput("GET /api/products/{id}", "catalog", "miss");
        collector.RecordDataCache("GET /api/products/{id}", "catalog", "hit");
        collector.RecordDataCache("GET /api/products/{id}", "catalog", "miss");
        collector.RecordInvalidation("catalog");

        AdminLiveStatsRawSnapshot raw = collector.GetRawSnapshot();
        raw.InstanceId.Should().Be("raw-1");
        AdminDomainCountersDto domain = raw.Domains.Should().ContainSingle().Subject;
        domain.Name.Should().Be("catalog");
        domain.OutputCacheHits.Should().Be(1);
        domain.OutputCacheMisses.Should().Be(1);
        domain.DataCacheHits.Should().Be(1);
        domain.DataCacheMisses.Should().Be(1);
        domain.FactoryRuns.Should().Be(1);
        domain.Invalidations.Should().Be(1);
        domain.FactoryDurationCount.Should().Be(0);
        domain.FactoryDurationSumMs.Should().BeNull();

        AdminEndpointCountersDto ep = raw.Endpoints.Should().ContainSingle().Subject;
        ep.Route.Should().Be("GET /api/products/{id}");
        ep.OutputCacheHits.Should().Be(1);
        ep.FactoryRuns.Should().Be(1);
    }

    [Fact]
    public void TrackLatency_RecordsFactoryPathOnly_NotHits()
    {
        InMemoryAdminStatsCollector collector = new(
            new CacheOrchestratorOptions.AdminOptions
            {
                Enabled = true,
                TrackEndpoints = true,
                TrackLatency = true
            },
            instanceId: "lat");

        long hitTicks = Stopwatch.Frequency / 1000; // ~1 ms
        long missTicks = Stopwatch.Frequency / 100; // ~10 ms

        collector.RecordDataCache("GET /x", "d", "hit", hitTicks);
        collector.RecordDataCache("GET /x", "d", "miss", missTicks);
        collector.RecordDataCache("GET /x", "d", "stale", missTicks);

        AdminDomainCountersDto d = collector.GetRawSnapshot().Domains.Single();
        d.FactoryDurationCount.Should().Be(2); // miss + stale, not hit
        d.FactoryDurationSumMs.Should().NotBeNull();
        d.FactoryDurationSumMs.Value.Should().BeApproximately(20.0, 2.0);
    }

    [Fact]
    public void OffAndBypassAndUnresolved_CountAsFactoryRuns()
    {
        InMemoryAdminStatsCollector collector = new(
            new CacheOrchestratorOptions.AdminOptions
            {
                Enabled = true,
                TrackEndpoints = true
            },
            instanceId: "off");

        collector.RecordOutput("GET /x", "d", "off");
        collector.RecordDataCache("GET /x", "d", "off");
        collector.RecordDataCache("GET /x", "d", "unresolved");
        collector.RecordDataCache("GET /x", "d", "bypass");

        AdminDomainCountersDto d = collector.GetRawSnapshot().Domains.Single();
        d.OutputCacheOff.Should().Be(1);
        d.OutputCacheBypass.Should().Be(0);
        d.DataCacheBypass.Should().Be(1);
        d.FactoryRuns.Should().Be(3);
        d.FactoryFailures.Should().Be(0);
    }

    [Fact]
    public void StaleAndFail_CountAsFactoryRunsAndFailures()
    {
        InMemoryAdminStatsCollector collector = new(
            new CacheOrchestratorOptions.AdminOptions
            {
                Enabled = true,
                TrackEndpoints = true
            },
            instanceId: "stale");

        collector.RecordDataCache("GET /x", "d", "miss");
        collector.RecordDataCache("GET /x", "d", "stale");
        collector.RecordDataCache("GET /x", "d", "fail");

        AdminDomainCountersDto d = collector.GetRawSnapshot().Domains.Single();
        d.DataCacheMisses.Should().Be(1);
        d.DataCacheStale.Should().Be(1);
        d.FactoryRuns.Should().Be(3);
        d.FactoryFailures.Should().Be(2);
    }

    [Fact]
    public void TrackResultSize_RecordsMissOnly()
    {
        InMemoryAdminStatsCollector collector = new(
            new CacheOrchestratorOptions.AdminOptions
            {
                Enabled = true,
                TrackEndpoints = true,
                TrackResultSize = true
            },
            instanceId: "sz");

        collector.RecordDataCache("GET /x", "d", "hit", resultSizeBytes: 999);
        collector.RecordDataCache("GET /x", "d", "miss", resultSizeBytes: 100);
        collector.RecordDataCache("GET /x", "d", "miss", resultSizeBytes: 300);
        collector.RecordDataCache("GET /x", "d", "stale", resultSizeBytes: 500);

        AdminDomainCountersDto d = collector.GetRawSnapshot().Domains.Single();
        d.FactoryResultSizeCount.Should().Be(2);
        d.FactoryResultSizeSumBytes.Should().Be(400);
    }

    [Fact]
    public void NoOpCollector_IsDisabled()
    {
        NoOpAdminStatsCollector.Instance.IsEnabled.Should().BeFalse();
        NoOpAdminStatsCollector.Instance.RecordOutput("GET /x", "d", "hit");
        NoOpAdminStatsCollector.Instance.GetRawSnapshot().Domains.Should().BeEmpty();
    }
}
