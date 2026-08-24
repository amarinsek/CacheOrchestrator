using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Services.Hints;
using CacheOrchestrator.AdminConsole.Services.Metrics;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class LiveHintProjectorTests
{
    [Fact]
    public void EstimateRequests_RoundsRateTimesOneMinute()
    {
        LiveHintProjector.EstimateRequests(0).Should().Be(0);
        LiveHintProjector.EstimateRequests(-1).Should().Be(0);
        LiveHintProjector.EstimateRequests(1).Should().Be(60);
        LiveHintProjector.EstimateRequests(0.5).Should().Be(30);
    }

    [Fact]
    public void ToDomainStats_ProjectsSharesIntoLayers()
    {
        LiveEntityRateDto row = new()
        {
            Name = "catalog",
            RequestRate = 1,
            OcHitShare = 0.8,
            FcHitShare = 0.1,
            FactoryShare = 0.2,
            FactoryFailShare = 0,
        };
        AdminDomainConfigDto cfg = new()
        {
            Name = "catalog",
            Version = "v2",
            DataCacheInstanceName = "default",
            VersionIsRuntimeOverride = true,
        };

        AdminDomainStatsDto stats = LiveHintProjector.ToDomainStats(row, cfg);
        stats.Name.Should().Be("catalog");
        stats.Version.Should().Be("v2");
        stats.VersionIsRuntimeOverride.Should().BeTrue();
        stats.Requests.Should().Be(60);
        stats.Oc.Hits.Should().Be(48);
        stats.Pipeline.FactoryShare.Should().BeApproximately(0.2, 0.05);
    }

    [Fact]
    public void ToQuietDomainStats_HasZeroTraffic()
    {
        AdminDomainStatsDto quiet = LiveHintProjector.ToQuietDomainStats(new AdminDomainConfigDto
        {
            Name = "idle",
            Version = "1",
            DataCacheInstanceName = "default",
        });
        quiet.Requests.Should().Be(0);
        quiet.Oc.Hits.Should().Be(0);
        quiet.Fc.Hits.Should().Be(0);
    }

    [Fact]
    public void Evaluate_RunsEngineOnLiveAndQuietDomains()
    {
        HintEngine engine = TestHintEngine.Create();
        LiveEntityRateDto hot = new()
        {
            Name = "hot",
            RequestRate = 1,
            OcHitShare = 0.1,
            FcHitShare = 0.1,
            FactoryShare = 0.7,
            FactoryFailShare = 0,
        };
        Dictionary<string, AdminDomainConfigDto> config = new(StringComparer.Ordinal)
        {
            ["hot"] = new AdminDomainConfigDto
            {
                Name = "hot",
                Version = "1",
                DataCacheInstanceName = "default",
            },
            ["idle"] = new AdminDomainConfigDto
            {
                Name = "idle",
                Version = "1",
                DataCacheInstanceName = "default",
            },
        };

        AdminHintSummaryDto summary = LiveHintProjector.Evaluate(
            engine,
            domains: [hot],
            endpoints: [],
            quietDomains: ["idle"],
            configByName: config);

        (summary.Info + summary.Warning + summary.Critical).Should().BeGreaterThan(0);
    }
}
