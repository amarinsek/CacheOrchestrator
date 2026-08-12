using CacheOrchestrator.Admin;
using CacheOrchestrator.Admin.App.Services;

namespace CacheOrchestrator.UnitTests.Admin;

public class StatsAggregatorTests
{
    [Fact]
    public void MergeDomains_SumsCountersAcrossInstances()
    {
        AdminLiveStatsSnapshot a = Snapshot("i1",
            Domain("catalog", ocHits: 10, ocMisses: 2, fcHits: 8, fcMisses: 1));
        AdminLiveStatsSnapshot b = Snapshot("i2",
            Domain("catalog", ocHits: 5, ocMisses: 3, fcHits: 4, fcMisses: 2));

        IReadOnlyList<AdminDomainStatsDto> merged = StatsAggregator.MergeDomains([a, b]);

        AdminDomainStatsDto catalog = merged.Should().ContainSingle().Subject;
        catalog.Name.Should().Be("catalog");
        catalog.Oc.Hits.Should().Be(15);
        catalog.Oc.Misses.Should().Be(5);
        catalog.Oc.HitRate.Should().BeApproximately(15.0 / 20.0, 0.0001);
        catalog.Fc.Hits.Should().Be(12);
        catalog.Fc.Misses.Should().Be(3);
    }

    [Fact]
    public void MergeDomains_MergesNestedEndpointsByRoute()
    {
        AdminDomainStatsDto d1 = Domain("catalog", 1, 0, 1, 0);
        d1 = WithEndpoint(d1, "GET /a", ocHits: 3, ocMisses: 1);
        AdminDomainStatsDto d2 = Domain("catalog", 0, 0, 0, 0);
        d2 = WithEndpoint(d2, "GET /a", ocHits: 2, ocMisses: 2);

        IReadOnlyList<AdminDomainStatsDto> merged = StatsAggregator.MergeDomains(
        [
            Snapshot("i1", d1),
            Snapshot("i2", d2)
        ]);

        AdminEndpointStatsDto ep = merged.Single().Endpoints.Should().ContainSingle().Subject;
        ep.Route.Should().Be("GET /a");
        ep.Oc.Hits.Should().Be(5);
        ep.Oc.Misses.Should().Be(3);
    }

    [Fact]
    public void MergeUnassignedEndpoints_SumsByRoute()
    {
        AdminLiveStatsSnapshot a = new()
        {
            InstanceId = "i1",
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Domains = [],
            UnassignedEndpoints =
            [
                Endpoint("GET /x", ocHits: 1, ocMisses: 1)
            ]
        };
        AdminLiveStatsSnapshot b = new()
        {
            InstanceId = "i2",
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Domains = [],
            UnassignedEndpoints =
            [
                Endpoint("GET /x", ocHits: 4, ocMisses: 0)
            ]
        };

        AdminEndpointStatsDto ep = StatsAggregator.MergeUnassignedEndpoints([a, b])
            .Should().ContainSingle().Subject;
        ep.Oc.Hits.Should().Be(5);
        ep.Oc.Misses.Should().Be(1);
    }

    private static AdminLiveStatsSnapshot Snapshot(string id, params AdminDomainStatsDto[] domains) =>
        new()
        {
            InstanceId = id,
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Domains = domains,
            UnassignedEndpoints = []
        };

    private static AdminDomainStatsDto Domain(
        string name,
        long ocHits,
        long ocMisses,
        long fcHits,
        long fcMisses) =>
        new()
        {
            Name = name,
            Version = "v1",
            Oc = new AdminLayerDto
            {
                Hits = ocHits,
                Misses = ocMisses,
                HitRate = HitRate(ocHits, ocMisses)
            },
            Fc = new AdminFusionLayerDto
            {
                Hits = fcHits,
                Misses = fcMisses,
                HitRate = HitRate(fcHits, fcMisses)
            },
            Endpoints = []
        };

    private static AdminDomainStatsDto WithEndpoint(
        AdminDomainStatsDto domain,
        string route,
        long ocHits,
        long ocMisses) =>
        new()
        {
            Name = domain.Name,
            Version = domain.Version,
            Oc = domain.Oc,
            Fc = domain.Fc,
            Endpoints =
            [
                Endpoint(route, ocHits, ocMisses, domain.Name)
            ]
        };

    private static AdminEndpointStatsDto Endpoint(
        string route,
        long ocHits,
        long ocMisses,
        string? domain = null) =>
        new()
        {
            Route = route,
            ConfiguredDomain = domain,
            Oc = new AdminLayerDto
            {
                Hits = ocHits,
                Misses = ocMisses,
                HitRate = HitRate(ocHits, ocMisses)
            },
            Fc = new AdminFusionLayerDto { Hits = 0, Misses = 0 }
        };

    private static double? HitRate(long hits, long misses)
    {
        long t = hits + misses;
        return t <= 0 ? null : (double)hits / t;
    }
}
