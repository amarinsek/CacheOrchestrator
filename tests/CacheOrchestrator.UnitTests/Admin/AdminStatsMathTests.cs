using CacheOrchestrator.Admin;

namespace CacheOrchestrator.UnitTests.Admin;

public class AdminStatsMathTests
{
    [Fact]
    public void OcHitDominates_FcMissShareIsSmallNotOne()
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
    public void FewRequests_LowRequestSampleOnShares()
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

    [Fact]
    public void Pipeline_includes_stale_share_and_excludes_it_from_other()
    {
        (_, _, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                ocHits: 80, ocMisses: 20, ocBypass: 0,
                fcHits: 10, fcMisses: 5, fcStale: 5, fcBypass: 0,
                factoryRuns: 5, factoryFailures: 5);

        fc.StaleShare.Should().BeApproximately(0.05, 0.0001);
        pipe.StaleShare.Should().BeApproximately(0.05, 0.0001);
        pipe.FactoryShare.Should().BeApproximately(0.05, 0.0001);
        // 100 - 80 ocHit - 10 fcHit - 5 stale - 5 factory = 0 other
        (pipe.OtherShare ?? 0).Should().BeApproximately(0, 0.0001);
    }
}
