using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using System.Diagnostics;

namespace CacheOrchestrator.Core.UnitTests.Admin;

public class InMemoryAdminStatsCollectorTests
{
    [Fact]
    public void RecordOutputAndFusion_AggregatesPerDomainAndEndpoint_WithShares_V1()
    {
        InMemoryAdminStatsCollector collector = new(
            new CacheOrchestratorOptions.AdminOptions
            {
                Enabled = true,
                TrackEndpoints = true
            },
            instanceId: "test-1");

        collector.RecordOutput("GET /api/products/{id}", "catalog", "hit");
        collector.RecordOutput("GET /api/products/{id}", "catalog", "miss");
        collector.RecordDataCache("GET /api/products/{id}", "catalog", "hit");
        collector.RecordDataCache("GET /api/products/{id}", "catalog", "miss");
        collector.RecordInvalidation("catalog");

        AdminLiveStatsSnapshot snap = collector.GetSnapshot();
        snap.InstanceId.Should().Be("test-1");
        snap.Domains.Should().ContainSingle(d => d.Name == "catalog");

        AdminDomainStatsDto domain = snap.Domains.Single(d => d.Name == "catalog");
        domain.Requests.Should().Be(2); // OC hit + OC miss
        domain.OutputCache.Hits.Should().Be(1);
        domain.OutputCache.Misses.Should().Be(1);
        domain.OutputCache.HitShare.Should().BeApproximately(0.5, 0.001);
        domain.OutputCache.HitRate.Should().BeApproximately(0.5, 0.001);
        domain.DataCache.Hits.Should().Be(1);
        domain.DataCache.Misses.Should().Be(1);
        domain.DataCache.HitShare.Should().BeApproximately(0.5, 0.001);
        domain.DataCache.FactoryRuns.Should().Be(1);
        domain.Invalidations.Should().Be(1);
        domain.LastInvalidationUtc.Should().NotBeNull();

        AdminEndpointStatsDto? ep = snap.UnassignedEndpoints.SingleOrDefault(e => e.Route == "GET /api/products/{id}");
        ep.Should().NotBeNull();
        ep!.OutputCache.Hits.Should().Be(1);
        ep.DataCache.Misses.Should().Be(1);
        ep.ConfiguredDomain.Should().Be("catalog");
        ep.Pipeline.OutputCacheHitShare.Should().BeApproximately(0.5, 0.001);
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
    public void RawAndV1_Parity_OnOutcomeCounters()
    {
        InMemoryAdminStatsCollector collector = new(
            new CacheOrchestratorOptions.AdminOptions
            {
                Enabled = true,
                TrackEndpoints = true
            },
            instanceId: "parity");

        collector.RecordOutput("GET /a", "d", "hit");
        collector.RecordOutput("GET /a", "d", "miss");
        collector.RecordOutput("GET /a", "d", "bypass");
        collector.RecordDataCache("GET /a", "d", "hit");
        collector.RecordDataCache("GET /a", "d", "miss");
        collector.RecordDataCache("GET /a", "d", "stale");
        collector.RecordDataCache("GET /a", "d", "bypass");

        AdminLiveStatsRawSnapshot raw = collector.GetRawSnapshot();
        AdminLiveStatsSnapshot v1 = collector.GetSnapshot();
        AdminLiveStatsSnapshot mapped = AdminStatsV1Mapper.ToLiveSnapshot(raw);

        AdminDomainCountersDto rd = raw.Domains.Single();
        AdminDomainStatsDto fd = v1.Domains.Single();
        AdminDomainStatsDto md = mapped.Domains.Single();

        fd.OutputCache.Hits.Should().Be(rd.OutputCacheHits);
        fd.OutputCache.Misses.Should().Be(rd.OutputCacheMisses);
        fd.OutputCache.Bypass.Should().Be(rd.OutputCacheBypass);
        fd.DataCache.Hits.Should().Be(rd.DataCacheHits);
        fd.DataCache.Misses.Should().Be(rd.DataCacheMisses);
        fd.DataCache.Stale.Should().Be(rd.DataCacheStale);
        fd.DataCache.Bypass.Should().Be(rd.DataCacheBypass);
        fd.DataCache.FactoryRuns.Should().Be(rd.FactoryRuns);
        fd.DataCache.FactoryFailures.Should().Be(rd.FactoryFailures);

        md.OutputCache.Hits.Should().Be(fd.OutputCache.Hits);
        md.DataCache.FactoryRuns.Should().Be(fd.DataCache.FactoryRuns);
        md.Requests.Should().Be(fd.Requests);
        md.Pipeline.FactoryShare.Should().Be(fd.Pipeline.FactoryShare);
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
        d.FactoryDurationSumMs!.Value.Should().BeApproximately(20.0, 2.0);
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
    public void OutputCacheHitsDominate_FcLayerMissRateIsNotShownAsRequestShare()
    {
        InMemoryAdminStatsCollector collector = new(
            new CacheOrchestratorOptions.AdminOptions
            {
                Enabled = true,
                TrackEndpoints = true
            },
            instanceId: "x");

        for (int i = 0; i < 99; i++)
            collector.RecordOutput("GET /hello", "hello", "hit");
        collector.RecordOutput("GET /hello", "hello", "miss");
        collector.RecordDataCache("GET /hello", "hello", "miss");

        AdminDomainStatsDto d = collector.GetSnapshot().Domains.Single();
        d.OutputCache.HitShare.Should().BeApproximately(0.99, 0.001);
        d.DataCache.MissRate.Should().Be(1.0);
        d.DataCache.MissShare.Should().BeApproximately(0.01, 0.001);
    }

    [Fact]
    public void NoOpCollector_IsDisabled()
    {
        NoOpAdminStatsCollector.Instance.IsEnabled.Should().BeFalse();
        NoOpAdminStatsCollector.Instance.RecordOutput("GET /x", "d", "hit");
        NoOpAdminStatsCollector.Instance.GetSnapshot().Domains.Should().BeEmpty();
        NoOpAdminStatsCollector.Instance.GetRawSnapshot().Domains.Should().BeEmpty();
    }
}
