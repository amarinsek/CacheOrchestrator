using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using System.Diagnostics;

namespace CacheOrchestrator.UnitTests.Admin;

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
        collector.RecordFusion("GET /api/products/{id}", "catalog", "hit");
        collector.RecordFusion("GET /api/products/{id}", "catalog", "miss");
        collector.RecordInvalidation("catalog");

        AdminLiveStatsSnapshot snap = collector.GetSnapshot();
        snap.InstanceId.Should().Be("test-1");
        snap.Domains.Should().ContainSingle(d => d.Name == "catalog");

        AdminDomainStatsDto domain = snap.Domains.Single(d => d.Name == "catalog");
        domain.Requests.Should().Be(2); // OC hit + OC miss
        domain.Oc.Hits.Should().Be(1);
        domain.Oc.Misses.Should().Be(1);
        domain.Oc.HitShare.Should().BeApproximately(0.5, 0.001);
        domain.Oc.HitRate.Should().BeApproximately(0.5, 0.001);
        domain.Fc.Hits.Should().Be(1);
        domain.Fc.Misses.Should().Be(1);
        domain.Fc.HitShare.Should().BeApproximately(0.5, 0.001);
        domain.Fc.FactoryRuns.Should().Be(1);
        domain.Invalidations.Should().Be(1);
        domain.LastInvalidationUtc.Should().NotBeNull();

        AdminEndpointStatsDto? ep = snap.UnassignedEndpoints.SingleOrDefault(e => e.Route == "GET /api/products/{id}");
        ep.Should().NotBeNull();
        ep!.Oc.Hits.Should().Be(1);
        ep.Fc.Misses.Should().Be(1);
        ep.ConfiguredDomain.Should().Be("catalog");
        ep.Pipeline.OcHitShare.Should().BeApproximately(0.5, 0.001);
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
        collector.RecordFusion("GET /api/products/{id}", "catalog", "hit");
        collector.RecordFusion("GET /api/products/{id}", "catalog", "miss");
        collector.RecordInvalidation("catalog");

        AdminLiveStatsRawSnapshot raw = collector.GetRawSnapshot();
        raw.InstanceId.Should().Be("raw-1");
        AdminDomainCountersDto domain = raw.Domains.Should().ContainSingle().Subject;
        domain.Name.Should().Be("catalog");
        domain.OcHits.Should().Be(1);
        domain.OcMisses.Should().Be(1);
        domain.FcHits.Should().Be(1);
        domain.FcMisses.Should().Be(1);
        domain.FactoryRuns.Should().Be(1);
        domain.Invalidations.Should().Be(1);
        domain.FactoryDurationCount.Should().Be(0);
        domain.FactoryDurationSumMs.Should().BeNull();

        AdminEndpointCountersDto ep = raw.Endpoints.Should().ContainSingle().Subject;
        ep.Route.Should().Be("GET /api/products/{id}");
        ep.OcHits.Should().Be(1);
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
        collector.RecordFusion("GET /a", "d", "hit");
        collector.RecordFusion("GET /a", "d", "miss");
        collector.RecordFusion("GET /a", "d", "stale");
        collector.RecordFusion("GET /a", "d", "bypass");

        AdminLiveStatsRawSnapshot raw = collector.GetRawSnapshot();
        AdminLiveStatsSnapshot v1 = collector.GetSnapshot();
        AdminLiveStatsSnapshot mapped = AdminStatsV1Mapper.ToLiveSnapshot(raw);

        AdminDomainCountersDto rd = raw.Domains.Single();
        AdminDomainStatsDto fd = v1.Domains.Single();
        AdminDomainStatsDto md = mapped.Domains.Single();

        fd.Oc.Hits.Should().Be(rd.OcHits);
        fd.Oc.Misses.Should().Be(rd.OcMisses);
        fd.Oc.Bypass.Should().Be(rd.OcBypass);
        fd.Fc.Hits.Should().Be(rd.FcHits);
        fd.Fc.Misses.Should().Be(rd.FcMisses);
        fd.Fc.Stale.Should().Be(rd.FcStale);
        fd.Fc.Bypass.Should().Be(rd.FcBypass);
        fd.Fc.FactoryRuns.Should().Be(rd.FactoryRuns);
        fd.Fc.FactoryFailures.Should().Be(rd.FactoryFailures);

        md.Oc.Hits.Should().Be(fd.Oc.Hits);
        md.Fc.FactoryRuns.Should().Be(fd.Fc.FactoryRuns);
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

        collector.RecordFusion("GET /x", "d", "hit", hitTicks);
        collector.RecordFusion("GET /x", "d", "miss", missTicks);
        collector.RecordFusion("GET /x", "d", "stale", missTicks);

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
        collector.RecordFusion("GET /x", "d", "off");
        collector.RecordFusion("GET /x", "d", "unresolved");
        collector.RecordFusion("GET /x", "d", "bypass");

        AdminDomainCountersDto d = collector.GetRawSnapshot().Domains.Single();
        d.OcOff.Should().Be(1);
        d.OcBypass.Should().Be(0);
        d.FcBypass.Should().Be(1);
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

        collector.RecordFusion("GET /x", "d", "miss");
        collector.RecordFusion("GET /x", "d", "stale");
        collector.RecordFusion("GET /x", "d", "fail");

        AdminDomainCountersDto d = collector.GetRawSnapshot().Domains.Single();
        d.FcMisses.Should().Be(1);
        d.FcStale.Should().Be(1);
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

        collector.RecordFusion("GET /x", "d", "hit", resultSizeBytes: 999);
        collector.RecordFusion("GET /x", "d", "miss", resultSizeBytes: 100);
        collector.RecordFusion("GET /x", "d", "miss", resultSizeBytes: 300);
        collector.RecordFusion("GET /x", "d", "stale", resultSizeBytes: 500);

        AdminDomainCountersDto d = collector.GetRawSnapshot().Domains.Single();
        d.FactoryResultSizeCount.Should().Be(2);
        d.FactoryResultSizeSumBytes.Should().Be(400);
    }

    [Fact]
    public void OcHitsDominate_FcLayerMissRateIsNotShownAsRequestShare()
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
        collector.RecordFusion("GET /hello", "hello", "miss");

        AdminDomainStatsDto d = collector.GetSnapshot().Domains.Single();
        d.Oc.HitShare.Should().BeApproximately(0.99, 0.001);
        d.Fc.MissRate.Should().Be(1.0);
        d.Fc.MissShare.Should().BeApproximately(0.01, 0.001);
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
