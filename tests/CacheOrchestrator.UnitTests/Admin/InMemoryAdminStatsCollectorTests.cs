using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.UnitTests.Admin;

public class InMemoryAdminStatsCollectorTests
{
    [Fact]
    public void RecordOutputAndFusion_AggregatesPerDomainAndEndpoint_WithShares()
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
    }
}
