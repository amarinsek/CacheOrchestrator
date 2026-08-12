using CacheOrchestrator.Admin;
using CacheOrchestrator.Admin.App.Services;

namespace CacheOrchestrator.UnitTests.Admin;

public class RecommendationHintsTests
{
    [Fact]
    public void ForEndpoint_HighOriginShare_EmitsWarning()
    {
        (_, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                ocHits: 10, ocMisses: 40, ocBypass: 0,
                fcHits: 5, fcMisses: 35, fcStale: 0, fcBypass: 0,
                factoryRuns: 35, factoryFailures: 0);

        AdminEndpointStatsDto ep = new()
        {
            Route = "GET /x",
            Requests = 50,
            Oc = oc,
            Fc = fc,
            Pipeline = pipe
        };

        IReadOnlyList<AdminHintDto> hints = RecommendationHints.ForEndpoint(ep);
        hints.Should().Contain(h => h.Code == "high-origin-share" && h.Severity == "Warning");
    }

    [Fact]
    public void ForDomain_ClientTtlMuchLargerThanOutput_EmitsInfo()
    {
        (_, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(50, 0, 0, 0, 0, 0, 0, 0, 0);

        AdminDomainStatsDto domain = new()
        {
            Name = "catalog",
            Version = "1",
            Requests = 50,
            Oc = oc,
            Fc = fc,
            Pipeline = pipe
        };

        AdminDomainConfigDto cfg = new()
        {
            Name = "catalog",
            Version = "1",
            FusionCacheInstanceName = "default",
            OutputCacheTtlSeconds = 60,
            ClientTtlSeconds = 3600,
            ClientTtlMinSeconds = 10
        };

        IReadOnlyList<AdminHintDto> hints = RecommendationHints.ForDomain(domain, cfg);
        hints.Should().Contain(h => h.Code == "client-ttl-gt-output");
    }
}
