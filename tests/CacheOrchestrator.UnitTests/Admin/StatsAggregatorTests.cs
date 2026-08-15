using CacheOrchestrator.Admin;
using CacheOrchestrator.Admin.App.Services;

namespace CacheOrchestrator.UnitTests.Admin;

public class StatsAggregatorTests
{
    [Fact]
    public void MergeDomains_SumsCountersAndRecomputesShares()
    {
        AdminLiveStatsSnapshot a = Snapshot("i1",
            Domain("catalog", ocHits: 90, ocMisses: 10, fcHits: 8, fcMisses: 2));
        AdminLiveStatsSnapshot b = Snapshot("i2",
            Domain("catalog", ocHits: 50, ocMisses: 50, fcHits: 40, fcMisses: 10));

        IReadOnlyList<AdminDomainStatsDto> merged = StatsAggregator.MergeDomains([a, b]);

        AdminDomainStatsDto catalog = merged.Should().ContainSingle().Subject;
        catalog.Name.Should().Be("catalog");
        catalog.Requests.Should().Be(200); // 100+100 OC traffic
        catalog.Oc.Hits.Should().Be(140);
        catalog.Oc.Misses.Should().Be(60);
        catalog.Oc.HitShare.Should().BeApproximately(0.7, 0.0001);
        catalog.Fc.Hits.Should().Be(48);
        catalog.Fc.MissShare.Should().BeApproximately(12.0 / 200.0, 0.0001);
        // Layer FC miss rate among FC traffic only (48 hit + 12 miss)
        catalog.Fc.MissRate.Should().BeApproximately(12.0 / 60.0, 0.0001);
    }

    [Fact]
    public void MergeDomains_WithByInstance_AttachesRowsAndSpread()
    {
        AdminLiveStatsSnapshot a = Snapshot("i1",
            Domain("catalog", ocHits: 100, ocMisses: 0, fcHits: 0, fcMisses: 0));
        AdminLiveStatsSnapshot b = Snapshot("i2",
            Domain("catalog", ocHits: 0, ocMisses: 100, fcHits: 0, fcMisses: 100));

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
        AdminDomainStatsDto d1 = Domain("catalog", 10, 0, 0, 0);
        d1 = WithEndpoint(d1, "GET /a", ocHits: 10, ocMisses: 0);
        AdminDomainStatsDto d2 = Domain("catalog", 5, 5, 0, 0);
        d2 = WithEndpoint(d2, "GET /a", ocHits: 5, ocMisses: 5);

        IReadOnlyList<AdminEndpointStatsDto> eps = StatsAggregator.MergeEndpoints(
        [
            Snapshot("i1", d1),
            Snapshot("i2", d2)
        ]);

        AdminEndpointStatsDto ep = eps.Should().ContainSingle().Subject;
        ep.Route.Should().Be("GET /a");
        ep.Requests.Should().Be(20);
        ep.Oc.HitShare.Should().BeApproximately(0.75, 0.0001);
    }

    [Fact]
    public void AdminStatsMath_OcHitDominates_FcMissShareIsSmallNotOne()
    {
        // 99 OC hits, 1 OC miss that is FC miss → FC layer miss rate 100%, miss share 1%
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

        // Enough total requests → request shares are not "low sample"
        oc.LowRequestSample.Should().BeFalse();
        fc.LowRequestSample.Should().BeFalse();
        // Only 1 FC hit+miss → layer rates are low-sample (noisy), shares still fine
        oc.LowSample.Should().BeFalse(); // OC layer n = 100
        fc.LowSample.Should().BeTrue();  // FC layer n = 1
    }

    [Fact]
    public void AdminStatsMath_FewRequests_LowRequestSampleOnShares()
    {
        (_, AdminLayerDto oc, AdminFusionLayerDto fc, _) =
            AdminStatsMath.BuildAll(
                ocHits: 5, ocMisses: 2, ocBypass: 0,
                fcHits: 1, fcMisses: 1, fcStale: 0, fcBypass: 0,
                factoryRuns: 1, factoryFailures: 0);

        oc.LowRequestSample.Should().BeTrue();  // 7 requests
        fc.LowRequestSample.Should().BeTrue();
        oc.LowSample.Should().BeTrue();         // OC layer n = 7
        fc.LowSample.Should().BeTrue();         // FC layer n = 2
    }

    private static AdminLiveStatsSnapshot Snapshot(string id, params AdminDomainStatsDto[] domains) =>
        new()
        {
            InstanceId = id,
            CollectedAtUtc = DateTimeOffset.UtcNow,
            Domains = domains,
            UnassignedEndpoints = [],
            Endpoints = domains.SelectMany(d => d.Endpoints).ToArray()
        };

    private static AdminDomainStatsDto Domain(
        string name,
        long ocHits,
        long ocMisses,
        long fcHits,
        long fcMisses)
    {
        (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(ocHits, ocMisses, 0, fcHits, fcMisses, 0, 0, fcMisses, 0);
        return new AdminDomainStatsDto
        {
            Name = name,
            InstanceId = null,
            Version = "v1",
            Requests = requests,
            Oc = oc,
            Fc = fc,
            Pipeline = pipe,
            Endpoints = []
        };
    }

    private static AdminDomainStatsDto WithEndpoint(
        AdminDomainStatsDto domain,
        string route,
        long ocHits,
        long ocMisses)
    {
        (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(ocHits, ocMisses, 0, 0, 0, 0, 0, 0, 0);
        return new AdminDomainStatsDto
        {
            Name = domain.Name,
            Version = domain.Version,
            Requests = domain.Requests,
            Oc = domain.Oc,
            Fc = domain.Fc,
            Pipeline = domain.Pipeline,
            Endpoints =
            [
                new AdminEndpointStatsDto
                {
                    Route = route,
                    ConfiguredDomain = domain.Name,
                    Requests = requests,
                    Oc = oc,
                    Fc = fc,
                    Pipeline = pipe
                }
            ]
        };
    }
}
