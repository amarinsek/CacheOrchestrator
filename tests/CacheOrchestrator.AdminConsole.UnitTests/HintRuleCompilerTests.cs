using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Services.Hints;
using CacheOrchestrator.AdminConsole.Services.Hints.Declarative;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class HintRuleCompilerTests
{
    private readonly HintRuleCompiler _compiler = new();

    [Fact]
    public void Compile_ValidRule_Succeeds()
    {
        const string json = """
            {
              "rules": [
                {
                  "code": "team-origin",
                  "severity": "Warning",
                  "scope": "domain",
                  "when": {
                    "all": [
                      { "path": "domain.requests", "op": ">=", "value": 20 },
                      { "path": "domain.dataCache.originShare", "op": ">=", "value": 0.25 }
                    ]
                  },
                  "message": "Origin {domain.dataCache.originShare:p1} on {domain.name}"
                }
              ]
            }
            """;

        HintRuleCompileBatchResult result = _compiler.CompileFile("test.json", json);
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Rules.Should().ContainSingle(r => r.Code == "team-origin");
    }

    [Fact]
    public void Compile_BadgeLongerThan3_WarnsAndKeepsFirstThree()
    {
        const string json = """
            {
              "rules": [
                {
                  "code": "long-badge",
                  "badge": "TOOLONG",
                  "severity": "Info",
                  "scope": "domain",
                  "when": { "path": "domain.requests", "op": ">=", "value": 0 },
                  "message": "x"
                }
              ]
            }
            """;

        HintRuleCompileBatchResult result = _compiler.CompileFile("t.json", json);
        result.Success.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Level == "warning" && e.Path == "badge");
        result.Rules[0].Badge.Should().Be("TOO");
    }

    [Fact]
    public void Compile_BadgeThreeRunes_IncludingArrow_IsOk()
    {
        const string json = """
            {
              "rules": [
                {
                  "code": "fa-up",
                  "badge": "FA↑",
                  "severity": "Warning",
                  "scope": "domain",
                  "when": { "path": "domain.requests", "op": ">=", "value": 0 },
                  "message": "x"
                }
              ]
            }
            """;

        HintRuleCompileBatchResult result = _compiler.CompileFile("t.json", json);
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Rules[0].Badge.Should().Be("FA↑");
    }

    [Fact]
    public void Compile_UnknownPath_ReportsError()
    {
        const string json = """
            {
              "rules": [
                {
                  "code": "bad-path",
                  "severity": "Info",
                  "scope": "domain",
                  "when": { "path": "domain.notAField", "op": "gt", "value": 1 },
                  "message": "x"
                }
              ]
            }
            """;

        HintRuleCompileBatchResult result = _compiler.CompileFile("bad.json", json);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.RuleCode == "bad-path"
            && e.Path == "when.path"
            && e.Message.Contains("Unknown path"));
    }

    [Fact]
    public void Compile_InvalidJson_ReportsError()
    {
        HintRuleCompileBatchResult result = _compiler.CompileFile("x.json", "{ not json");
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "$");
    }

    [Fact]
    public void DeclarativeRule_EvaluatesAgainstDomain()
    {
        const string json = """
            {
              "rules": [
                {
                  "code": "team-origin",
                  "severity": "Warning",
                  "scope": "domain",
                  "when": {
                    "all": [
                      { "path": "domain.requests", "op": ">=", "value": 20 },
                      { "path": "domain.dataCache.originShare", "op": ">=", "value": 0.25 }
                    ]
                  },
                  "message": "Origin {domain.dataCache.originShare:p0}"
                }
              ]
            }
            """;

        HintRuleCompileBatchResult compiled = _compiler.CompileFile("t.json", json);
        compiled.Success.Should().BeTrue();
        IHintRule rule = compiled.Rules[0];

        (_, AdminLayerDto outputCache, AdminDataCacheLayerDto dataCache, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                outputCacheHits: 20, outputCacheMisses: 30, outputCacheBypass: 0,
                dataCacheHits: 15, dataCacheMisses: 15, dataCacheStale: 0, dataCacheBypass: 0,
                factoryRuns: 15, factoryFailures: 0);

        var ctx = new HintEvaluationContext
        {
            NowUtc = DateTimeOffset.UtcNow,
            Domain = new AdminDomainStatsDto
            {
                Name = "maps",
                Version = "1",
                Requests = 50,
                OutputCache = outputCache,
                DataCache = dataCache,
                Pipeline = pipe
            }
        };

        IReadOnlyList<AdminHintDto> hints = rule.Evaluate(ctx).ToList();
        hints.Should().ContainSingle(h => h.Code == "team-origin" && h.Severity == "Warning");
        hints[0].Message.Should().Contain("%");
    }

    [Fact]
    public void Compile_EmptyRules_Fails()
    {
        HintRuleCompileBatchResult result = _compiler.CompileFile("empty.json", """{"rules":[]}""");
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "rules");
    }

    [Fact]
    public void Compile_MissingCode_Fails()
    {
        const string json = """
            {
              "rules": [
                { "severity": "Info", "scope": "domain", "when": { "path": "domain.requests", "op": ">", "value": 0 }, "message": "x" }
              ]
            }
            """;
        HintRuleCompileBatchResult result = _compiler.CompileFile("nocode.json", json);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path.Contains("code", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_AnyNot_Evaluates()
    {
        const string json = """
            {
              "rules": [
                {
                  "code": "any-not",
                  "severity": "Info",
                  "scope": "domain",
                  "when": {
                    "any": [
                      { "path": "domain.requests", "op": ">=", "value": 1000 },
                      { "not": { "path": "domain.version", "op": "eq", "value": "skip-me" } }
                    ]
                  },
                  "message": "matched"
                }
              ]
            }
            """;
        HintRuleCompileBatchResult compiled = _compiler.CompileFile("any.json", json);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        IHintRule rule = compiled.Rules[0];

        var ctx = new HintEvaluationContext
        {
            NowUtc = DateTimeOffset.UtcNow,
            Domain = new AdminDomainStatsDto
            {
                Name = "maps",
                Version = "1",
                Requests = 1,
                OutputCache = new AdminLayerDto(),
                DataCache = new AdminDataCacheLayerDto(),
                Pipeline = new AdminPipelineDto(),
            },
        };

        rule.Evaluate(ctx).Should().ContainSingle(h => h.Code == "any-not");
    }
}
