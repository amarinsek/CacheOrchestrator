using CacheOrchestrator.Admin;

namespace CacheOrchestrator.Core.UnitTests.Admin;

public class AdminStatsMathTests
{
    [Fact]
    public void OutputCacheHitDominates_DataCacheMissShareIsSmallNotOne()
    {
        (long requests, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 99, outputCacheMisses: 1, outputCacheBypass: 0,
                dataCacheHits: 0, dataCacheMisses: 1, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 1, factoryFailures: 0);

        requests.Should().Be(100);
        outputCache.HitShare.Should().BeApproximately(0.99, 0.0001);
        dataCache.MissRate.Should().BeApproximately(1.0, 0.0001);
        dataCache.MissShare.Should().BeApproximately(0.01, 0.0001);
        dataCache.FactoryShare.Should().BeApproximately(0.01, 0.0001);
        pipe.OutputCacheHitShare.Should().BeApproximately(0.99, 0.0001);

        outputCache.LowRequestSample.Should().BeFalse();
        dataCache.LowRequestSample.Should().BeFalse();
        outputCache.LowSample.Should().BeFalse();
        dataCache.LowSample.Should().BeTrue();
    }

    [Fact]
    public void FewRequests_LowRequestSampleOnShares()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, _) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 5, outputCacheMisses: 2, outputCacheBypass: 0,
                dataCacheHits: 1, dataCacheMisses: 1, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 1, factoryFailures: 0);

        outputCache.LowRequestSample.Should().BeTrue();
        dataCache.LowRequestSample.Should().BeTrue();
        outputCache.LowSample.Should().BeTrue();
        dataCache.LowSample.Should().BeTrue();
    }

    [Fact]
    public void Pipeline_stale_is_overlay_inside_factory_share()
    {
        (_, _, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 80, outputCacheMisses: 20, outputCacheBypass: 0,
                dataCacheHits: 10, dataCacheMisses: 5, dataCacheStale: 5, dataCacheBypass: 0,
                factoryRuns: 10, factoryFailures: 5);

        dataCache.StaleShare.Should().BeApproximately(0.05, 0.0001);
        pipe.StaleShare.Should().BeApproximately(0.05, 0.0001);
        pipe.FactoryShare.Should().BeApproximately(0.10, 0.0001);
        pipe.OutputCacheHitShare.Should().BeApproximately(0.80, 0.0001);
        pipe.DataCacheHitShare.Should().BeApproximately(0.10, 0.0001);
        // Exclusive: 80 OC hit + 10 FC hit + 10 FA run = 100
        (pipe.OtherShare ?? 0).Should().BeApproximately(0, 0.0001);
    }

    [Fact]
    public void BothLayersOff_WithFactoryRuns_IsAllFaRun()
    {
        (long requests, AdminLayerDto outputCache, _, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 0, outputCacheMisses: 0, outputCacheBypass: 0,
                dataCacheHits: 0, dataCacheMisses: 0, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 50, factoryFailures: 0,
                outputCacheOff: 50);

        requests.Should().Be(50);
        outputCache.Off.Should().Be(50);
        outputCache.OffShare.Should().BeApproximately(1.0, 0.0001);
        pipe.OutputCacheHitShare.Should().BeApproximately(0, 0.0001);
        pipe.DataCacheHitShare.Should().BeApproximately(0, 0.0001);
        pipe.FactoryShare.Should().BeApproximately(1.0, 0.0001);
        (pipe.OtherShare ?? 0).Should().BeApproximately(0, 0.0001);
    }

    [Fact]
    public void AuthBypass_WithFactoryRuns_IsFaRunNotMixBypass()
    {
        (long requests, _, _, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 0, outputCacheMisses: 0, outputCacheBypass: 100,
                dataCacheHits: 0, dataCacheMisses: 0, dataCacheStale: 0, dataCacheBypass: 100,
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
                outputCacheHits: 0, outputCacheMisses: 0, outputCacheBypass: 0,
                dataCacheHits: 0, dataCacheMisses: 0, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 25, factoryFailures: 0);

        requests.Should().Be(25);
        pipe.FactoryShare.Should().BeApproximately(1.0, 0.0001);
    }
}
