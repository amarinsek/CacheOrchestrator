using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.UnitTests.Admin;

public class InMemoryAdminStatsCollectorTests
{
    [Fact]
    public void RecordOutputAndFusion_AggregatesPerDomainAndEndpoint()
    {
        InMemoryAdminStatsCollector collector = new(new CacheOrchestratorOptions.AdminOptions
        {
            Enabled = true,
            InstanceId = "test-1",
            TrackEndpoints = true
        });

        collector.RecordOutput("GET /api/products/{id}", "catalog", "hit");
        collector.RecordOutput("GET /api/products/{id}", "catalog", "miss");
        collector.RecordFusion("GET /api/products/{id}", "catalog", "hit");
        collector.RecordFusion("GET /api/products/{id}", "catalog", "miss");
        collector.RecordInvalidation("catalog");

        AdminLiveStatsSnapshot snap = collector.GetSnapshot();
        snap.InstanceId.Should().Be("test-1");
        snap.Domains.Should().ContainSingle(d => d.Name == "catalog");

        AdminDomainStatsDto domain = snap.Domains.Single(d => d.Name == "catalog");
        domain.Oc.Hits.Should().Be(1);
        domain.Oc.Misses.Should().Be(1);
        domain.Oc.HitRate.Should().BeApproximately(0.5, 0.001);
        domain.Fc.Hits.Should().Be(1);
        domain.Fc.Misses.Should().Be(1);
        domain.Fc.FactoryRuns.Should().Be(1);
        domain.Invalidations.Should().Be(1);
        domain.LastInvalidationUtc.Should().NotBeNull();

        AdminEndpointStatsDto? ep = snap.UnassignedEndpoints.SingleOrDefault(e => e.Route == "GET /api/products/{id}");
        ep.Should().NotBeNull();
        ep!.Oc.Hits.Should().Be(1);
        ep.Fc.Misses.Should().Be(1);
        ep.ConfiguredDomain.Should().Be("catalog");
    }

    [Fact]
    public void NoOpCollector_IsDisabled()
    {
        NoOpAdminStatsCollector.Instance.IsEnabled.Should().BeFalse();
        NoOpAdminStatsCollector.Instance.RecordOutput("GET /x", "d", "hit");
        NoOpAdminStatsCollector.Instance.GetSnapshot().Domains.Should().BeEmpty();
    }
}
