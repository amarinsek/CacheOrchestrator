using CacheOrchestrator.Admin;

namespace CacheOrchestrator.Core.UnitTests.Admin;

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
    public void Pipeline_stale_is_overlay_inside_factory_share()
    {
        (_, _, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                ocHits: 80, ocMisses: 20, ocBypass: 0,
                fcHits: 10, fcMisses: 5, fcStale: 5, fcBypass: 0,
                factoryRuns: 10, factoryFailures: 5);

        fc.StaleShare.Should().BeApproximately(0.05, 0.0001);
        pipe.StaleShare.Should().BeApproximately(0.05, 0.0001);
        pipe.FactoryShare.Should().BeApproximately(0.10, 0.0001);
        pipe.OcHitShare.Should().BeApproximately(0.80, 0.0001);
        pipe.FcHitShare.Should().BeApproximately(0.10, 0.0001);
        // Exclusive: 80 OC hit + 10 FC hit + 10 FA run = 100
        (pipe.OtherShare ?? 0).Should().BeApproximately(0, 0.0001);
    }

    [Fact]
    public void BothLayersOff_WithFactoryRuns_IsAllFaRun()
    {
        (long requests, AdminLayerDto oc, _, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                ocHits: 0, ocMisses: 0, ocBypass: 0,
                fcHits: 0, fcMisses: 0, fcStale: 0, fcBypass: 0,
                factoryRuns: 50, factoryFailures: 0,
                ocOff: 50);

        requests.Should().Be(50);
        oc.Off.Should().Be(50);
        oc.OffShare.Should().BeApproximately(1.0, 0.0001);
        pipe.OcHitShare.Should().BeApproximately(0, 0.0001);
        pipe.FcHitShare.Should().BeApproximately(0, 0.0001);
        pipe.FactoryShare.Should().BeApproximately(1.0, 0.0001);
        (pipe.OtherShare ?? 0).Should().BeApproximately(0, 0.0001);
    }

    [Fact]
    public void AuthBypass_WithFactoryRuns_IsFaRunNotMixBypass()
    {
        (long requests, _, _, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                ocHits: 0, ocMisses: 0, ocBypass: 100,
                fcHits: 0, fcMisses: 0, fcStale: 0, fcBypass: 100,
                factoryRuns: 100, factoryFailures: 0);

        requests.Should().Be(100);
        pipe.FactoryShare.Should().BeApproximately(1.0, 0.0001);
        pipe.BypassShare.Should().BeApproximately(1.0, 0.0001);
        (pipe.OtherShare ?? 0).Should().BeApproximately(0, 0.0001);
    }

    [Fact]
    public void FusionOnly_FactoryRunsWhenFcOff_UseFactoryAsDenominator()
    {
        (long requests, _, _, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                ocHits: 0, ocMisses: 0, ocBypass: 0,
                fcHits: 0, fcMisses: 0, fcStale: 0, fcBypass: 0,
                factoryRuns: 25, factoryFailures: 0);

        requests.Should().Be(25);
        pipe.FactoryShare.Should().BeApproximately(1.0, 0.0001);
    }
}
