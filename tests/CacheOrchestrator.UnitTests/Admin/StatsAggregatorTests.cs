using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Services;

namespace CacheOrchestrator.UnitTests.Admin;

public class StatsAggregatorTests
{
    [Fact]
    public void MergeDomains_SumsCountersAndRecomputesShares()
    {
        AdminLiveStatsRawSnapshot a = Snapshot("i1",
            Domain("catalog", ocHits: 90, ocMisses: 10, fcHits: 8, fcMisses: 2, factoryRuns: 2));
        AdminLiveStatsRawSnapshot b = Snapshot("i2",
            Domain("catalog", ocHits: 50, ocMisses: 50, fcHits: 40, fcMisses: 10, factoryRuns: 10));

        IReadOnlyList<AdminDomainStatsDto> merged = StatsAggregator.MergeDomains([a, b]);

        AdminDomainStatsDto catalog = merged.Should().ContainSingle().Subject;
        catalog.Name.Should().Be("catalog");
        catalog.Requests.Should().Be(200); // 100+100 OC traffic
        catalog.Oc.Hits.Should().Be(140);
        catalog.Oc.Misses.Should().Be(60);
        catalog.Oc.HitShare.Should().BeApproximately(0.7, 0.0001);
        catalog.Fc.Hits.Should().Be(48);
        catalog.Fc.MissShare.Should().BeApproximately(12.0 / 200.0, 0.0001);
        catalog.Fc.MissRate.Should().BeApproximately(12.0 / 60.0, 0.0001);
        catalog.Impact.Should().NotBeNull();
        catalog.Impact!.FactoryShare.Should().BeApproximately(12.0 / 200.0, 0.0001);
        catalog.Impact.FactoryAvoidance.Should().BeApproximately(1.0 - 12.0 / 200.0, 0.0001);
    }

    [Fact]
    public void MergeDomains_WithByInstance_AttachesRowsAndSpread()
    {
        AdminLiveStatsRawSnapshot a = Snapshot("i1",
            Domain("catalog", ocHits: 100, ocMisses: 0, fcHits: 0, fcMisses: 0, factoryRuns: 0));
        AdminLiveStatsRawSnapshot b = Snapshot("i2",
            Domain("catalog", ocHits: 0, ocMisses: 100, fcHits: 0, fcMisses: 100, factoryRuns: 100));

        AdminDomainStatsDto catalog = StatsAggregator.MergeDomains([a, b], includeByInstance: true)
            .Should().ContainSingle().Subject;

        catalog.ByInstance.Should().HaveCount(2);
        catalog.InstanceSpread.Should().NotBeNull();
        catalog.InstanceSpread!.OcHitShare!.Min.Should().BeApproximately(0, 0.001);
        catalog.InstanceSpread.OcHitShare.Max.Should().BeApproximately(1, 0.001);
    }

    [Fact]
    public void MergeEndpoints_IsFundamentalUnit()
    {
        AdminDomainCountersDto d1 = Domain("catalog", 10, 0, 0, 0, 0);
        d1 = WithEndpoint(d1, "GET /a", ocHits: 10, ocMisses: 0, factoryRuns: 0);
        AdminDomainCountersDto d2 = Domain("catalog", 5, 5, 0, 0, 0);
        d2 = WithEndpoint(d2, "GET /a", ocHits: 5, ocMisses: 5, factoryRuns: 0);

        IReadOnlyList<AdminEndpointStatsDto> eps = StatsAggregator.MergeEndpoints(
        [
            Snapshot("i1", d1),
            Snapshot("i2", d2)
        ]);

        AdminEndpointStatsDto ep = eps.Should().ContainSingle().Subject;
        ep.Route.Should().Be("GET /a");
        ep.Requests.Should().Be(20);
        ep.Oc.HitShare.Should().BeApproximately(0.75, 0.0001);
        ep.Impact.Should().NotBeNull();
    }

    [Fact]
    public void MergeDomains_SumsFactoryDuration_ForImpact()
    {
        AdminDomainCountersDto a = new()
        {
            Name = "d",
            Version = "1",
            OcHits = 100,
            FactoryDurationSumMs = 100,
            FactoryDurationCount = 10
        };
        AdminDomainCountersDto b = new()
        {
            Name = "d",
            Version = "1",
            OcHits = 100,
            FactoryDurationSumMs = 200,
            FactoryDurationCount = 10
        };

        AdminDomainStatsDto merged = StatsAggregator.MergeDomains([Snapshot("i1", a), Snapshot("i2", b)])
            .Single();

        merged.Impact!.FactoryDurationCount.Should().Be(20);
        merged.Impact.FactoryDurationSumMs.Should().BeApproximately(300, 0.01);
        merged.Impact.AvgFactoryDurationMs.Should().BeApproximately(15, 0.01);
    }

    [Fact]
    public void AdminStatsMath_OcHitDominates_FcMissShareIsSmallNotOne()
    {
        (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                ocHits: 99, ocMisses: 1, ocBypass: 0,
                fcHits: 0, fcMisses: 1, fcStale: 0, fcBypass: 0,
                factoryRuns: 1, factoryFailures: 0);

        requests.Should().Be(100);
        oc.HitShare.Should().BeApproximately(0.99, 0.0001);
        fc.MissRate.Should().BeApproximately(1.0, 0.0001);
        fc.MissShare.Should().BeApproximately(0.01, 0.0001);
        fc.FactoryShare.Should().BeApproximately(0.01, 0.0001);
        pipe.OcHitShare.Should().BeApproximately(0.99, 0.0001);

        oc.LowRequestSample.Should().BeFalse();
        fc.LowRequestSample.Should().BeFalse();
        oc.LowSample.Should().BeFalse();
        fc.LowSample.Should().BeTrue();
    }

    [Fact]
    public void AdminStatsMath_FewRequests_LowRequestSampleOnShares()
    {
        (_, AdminLayerDto oc, AdminFusionLayerDto fc, _) =
            AdminStatsMath.BuildAll(
                ocHits: 5, ocMisses: 2, ocBypass: 0,
                fcHits: 1, fcMisses: 1, fcStale: 0, fcBypass: 0,
                factoryRuns: 1, factoryFailures: 0);

        oc.LowRequestSample.Should().BeTrue();
        fc.LowRequestSample.Should().BeTrue();
        oc.LowSample.Should().BeTrue();
        fc.LowSample.Should().BeTrue();
    }

    private static AdminLiveStatsRawSnapshot Snapshot(string id, params AdminDomainCountersDto[] domains) =>
        new()
        {
            InstanceId = id,
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Domains = domains,
            UnassignedEndpoints = [],
            Endpoints = domains.SelectMany(d => d.Endpoints).ToArray()
        };

    private static AdminDomainCountersDto Domain(
        string name,
        long ocHits,
        long ocMisses,
        long fcHits,
        long fcMisses,
        long factoryRuns) =>
        new()
        {
            Name = name,
            Version = "1",
            OcHits = ocHits,
            OcMisses = ocMisses,
            FcHits = fcHits,
            FcMisses = fcMisses,
            FactoryRuns = factoryRuns,
            Endpoints = []
        };

    private static AdminDomainCountersDto WithEndpoint(
        AdminDomainCountersDto d,
        string route,
        long ocHits,
        long ocMisses,
        long factoryRuns) =>
        new()
        {
            Name = d.Name,
            Version = d.Version,
            OcHits = d.OcHits,
            OcMisses = d.OcMisses,
            FcHits = d.FcHits,
            FcMisses = d.FcMisses,
            FactoryRuns = d.FactoryRuns,
            Endpoints =
            [
                new AdminEndpointCountersDto
                {
                    Route = route,
                    ConfiguredDomain = d.Name,
                    OcHits = ocHits,
                    OcMisses = ocMisses,
                    FactoryRuns = factoryRuns
                }
            ]
        };
}
