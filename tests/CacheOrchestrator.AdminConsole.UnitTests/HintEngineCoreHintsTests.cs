using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Services.Hints;

namespace CacheOrchestrator.AdminConsole.UnitTests;

/// <summary>
/// Evaluates product <c>core-hints.json</c> through <see cref="HintEngine"/> (production path).
/// </summary>
public class HintEngineCoreHintsTests
{
    private readonly HintEngine _engine = TestHintEngine.Create();

    [Fact]
    public void Catalog_LoadsCorePack()
    {
        IReadOnlyList<HintRuleCatalogEntry> catalog = _engine.GetCatalog();
        catalog.Should().NotBeEmpty();
        catalog.Should().Contain(e => e.Code == "high-factory-share" && e.Scope == "domain");
        catalog.Should().Contain(e => e.Code == "high-factory-share" && e.Scope == "endpoint");
        catalog.Should().OnlyContain(e => e.IsBuiltIn);
    }

    [Fact]
    public void EvaluateDomain_HealthyOc_LowFcLayerRate_DoesNotWarn()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 32, outputCacheMisses: 2, outputCacheBypass: 0,
                dataCacheHits: 0, dataCacheMisses: 2, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 2, factoryFailures: 0);

        AdminDomainStatsDto domain = new()
        {
            Name = "maps",
            Version = "1",
            Requests = 34,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(domain, config: null);
        hints.Select(h => h.Severity).Should().NotContain("Warning").And.NotContain("Critical");
        hints.Should().NotContain(h => h.Code == "low-fc-hit-rate");
    }

    [Fact]
    public void EvaluateEndpoint_HighFactoryShare_EmitsWarning()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 20, outputCacheMisses: 30, outputCacheBypass: 0,
                dataCacheHits: 15, dataCacheMisses: 15, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 15, factoryFailures: 0);

        AdminEndpointStatsDto ep = new()
        {
            Route = "GET /x",
            Requests = 50,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateEndpoint(ep);
        hints.Should().Contain(h => h.Code == "high-factory-share" && h.Severity == "Warning");
    }

    [Fact]
    public void EvaluateDomain_FactoryDominates_EmitsCritical()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 10, outputCacheMisses: 40, outputCacheBypass: 0,
                dataCacheHits: 5, dataCacheMisses: 35, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 35, factoryFailures: 0);

        AdminDomainStatsDto domain = new()
        {
            Name = "hot",
            Version = "1",
            Requests = 50,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(domain, config: null);
        hints.Should().Contain(h => h.Code == "critical-factory-share" && h.Severity == "Critical");
    }

    [Fact]
    public void EvaluateDomain_ClientTtlMuchLargerThanOutput_WithoutSchedule_EmitsInfo()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(50, 0, 0, 0, 0, 0, 0, 0, 0);

        AdminDomainStatsDto domain = new()
        {
            Name = "catalog",
            Version = "1",
            Requests = 50,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        };

        AdminDomainConfigDto cfg = new()
        {
            Name = "catalog",
            Version = "1",
            DataCacheInstanceName = "default",
            OutputCacheTtlSeconds = 60,
            ClientTtlSeconds = 3600,
            ClientTtlMinSeconds = 10
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(domain, cfg);
        hints.Should().Contain(h => h.Code == "client-ttl-gt-output");
    }

    [Fact]
    public void EvaluateDomain_ClientTtlLargerThanOutput_WithSchedule_DoesNotEmit()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(50, 0, 0, 0, 0, 0, 0, 0, 0);

        AdminDomainStatsDto domain = new()
        {
            Name = "tiles",
            Version = "1",
            Requests = 50,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        };

        AdminDomainConfigDto cfg = new()
        {
            Name = "tiles",
            Version = "1",
            DataCacheInstanceName = "default",
            OutputCacheTtlSeconds = 300,
            ClientTtlSeconds = 2592000,
            ScheduledUpdateUtc = DateTimeOffset.UtcNow.AddDays(20),
            SchedulePhase = "calm"
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(domain, cfg);
        hints.Should().NotContain(h => h.Code == "client-ttl-gt-output");
    }

    [Fact]
    public void EvaluateDomain_ApproachingSchedule_EmitsInfo()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(50, 0, 0, 0, 0, 0, 0, 0, 0);

        AdminDomainConfigDto cfg = new()
        {
            Name = "tiles",
            Version = "1",
            DataCacheInstanceName = "default",
            ScheduledUpdateUtc = DateTimeOffset.UtcNow.AddHours(2),
            SchedulePhase = "approaching"
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(
            new() { Name = "tiles", Version = "1", Requests = 50, OutputCache = outputCache, DataCache = dataCache, Pipeline = pipe },
            cfg);

        hints.Should().Contain(h => h.Code == "schedule-approaching" && h.Severity == "Info");
        hints.Should().NotContain(h => h.Code == "schedule-phase");
        hints.Should().NotContain(h => h.Severity == "Warning" || h.Severity == "Critical");
    }

    [Fact]
    public void EvaluateDomain_HoldLongerThanADay_EmitsWarning()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(50, 0, 0, 0, 0, 0, 0, 0, 0);

        AdminDomainConfigDto cfg = new()
        {
            Name = "tiles",
            Version = "1",
            DataCacheInstanceName = "default",
            ScheduledUpdateUtc = DateTimeOffset.UtcNow.AddHours(-25),
            SchedulePhase = "hold"
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(
            new() { Name = "tiles", Version = "1", Requests = 50, OutputCache = outputCache, DataCache = dataCache, Pipeline = pipe },
            cfg);

        hints.Should().Contain(h => h.Code == "schedule-hold-lingering" && h.Severity == "Warning");
        hints.Should().NotContain(h => h.Code == "schedule-phase");
    }

    [Fact]
    public void EvaluateDomain_FactoryFailures_EmitsWarning()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 80, outputCacheMisses: 20, outputCacheBypass: 0,
                dataCacheHits: 5, dataCacheMisses: 15, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 20, factoryFailures: 4);

        AdminDomainStatsDto domain = new()
        {
            Name = "catalog",
            Version = "1",
            Requests = 100,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(domain, config: null);
        hints.Should().Contain(h => h.Code == "factory-failures" && h.Severity == "Warning");
    }

    [Fact]
    public void EvaluateDomain_MostFactoryRunsFail_EmitsCritical()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 80, outputCacheMisses: 20, outputCacheBypass: 0,
                dataCacheHits: 2, dataCacheMisses: 18, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 20, factoryFailures: 14);

        AdminDomainStatsDto domain = new()
        {
            Name = "catalog",
            Version = "1",
            Requests = 100,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(domain, config: null);
        hints.Should().Contain(h => h.Code == "critical-factory-failures" && h.Severity == "Critical");
    }

    [Fact]
    public void EvaluateDomain_FewFactoryFailures_DoesNotHint()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 80, outputCacheMisses: 20, outputCacheBypass: 0,
                dataCacheHits: 18, dataCacheMisses: 2, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 20, factoryFailures: 1);

        AdminDomainStatsDto domain = new()
        {
            Name = "catalog",
            Version = "1",
            Requests = 100,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(domain, config: null);
        hints.Should().NotContain(h =>
            h.Code == "factory-failures" || h.Code == "critical-factory-failures");
    }

    [Fact]
    public void EvaluateDomain_RuntimeVersionOverride_EmitsInfo()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(50, 0, 0, 0, 0, 0, 0, 0, 0);

        AdminDomainStatsDto domain = new()
        {
            Name = "catalog",
            Version = "bump-1",
            VersionIsRuntimeOverride = true,
            Requests = 50,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(domain, config: null);
        hints.Should().Contain(h => h.Code == "runtime-override" && h.Severity == "Info");
    }

    [Fact]
    public void EvaluateDomain_ScheduleCannotRamp_EmitsInfo()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(50, 0, 0, 0, 0, 0, 0, 0, 0);

        AdminDomainConfigDto cfg = new()
        {
            Name = "tiles",
            Version = "1",
            DataCacheInstanceName = "default",
            ClientTtlSeconds = 900,
            ClientTtlMinSeconds = 900,
            ScheduledUpdateUtc = DateTimeOffset.UtcNow.AddDays(1),
            SchedulePhase = "calm"
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(
            new() { Name = "tiles", Version = "1", Requests = 50, OutputCache = outputCache, DataCache = dataCache, Pipeline = pipe },
            cfg);

        hints.Should().Contain(h => h.Code == "schedule-flat" && h.Severity == "Info");
    }

    [Fact]
    public void EvaluateDomain_HoldSchedule_EmitsInfo()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(50, 0, 0, 0, 0, 0, 0, 0, 0);

        AdminDomainConfigDto cfg = new()
        {
            Name = "tiles",
            Version = "1",
            DataCacheInstanceName = "default",
            ScheduledUpdateUtc = DateTimeOffset.UtcNow.AddHours(-1),
            SchedulePhase = "hold"
        };

        IReadOnlyList<AdminHintDto> hints = _engine.EvaluateDomain(
            new() { Name = "tiles", Version = "1", Requests = 50, OutputCache = outputCache, DataCache = dataCache, Pipeline = pipe },
            cfg);

        hints.Should().Contain(h => h.Code == "schedule-phase" && h.Severity == "Info");
    }

    [Fact]
    public void Summarize_And_CollectFromStats_AggregateAttachedHints()
    {
        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 10, outputCacheMisses: 40, outputCacheBypass: 0,
                dataCacheHits: 5, dataCacheMisses: 35, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 35, factoryFailures: 0);

        AdminDomainStatsDto withHints = _engine.WithHints(new AdminDomainStatsDto
        {
            Name = "hot",
            Version = "1",
            Requests = 50,
            OutputCache = outputCache,
            DataCache = dataCache,
            Pipeline = pipe
        });

        IReadOnlyList<AdminHintDto> collected = HintEngine.CollectFromStats([withHints]);
        collected.Should().NotBeEmpty();

        AdminHintSummaryDto summary = HintEngine.Summarize(collected);
        summary.Critical.Should().BeGreaterThan(0);
    }
}
