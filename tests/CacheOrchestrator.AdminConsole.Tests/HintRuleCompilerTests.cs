using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Services.Hints;
using CacheOrchestrator.AdminConsole.Services.Hints.Declarative;

namespace CacheOrchestrator.AdminConsole.Tests;

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
                      { "path": "domain.fc.originShare", "op": ">=", "value": 0.25 }
                    ]
                  },
                  "message": "Origin {domain.fc.originShare:p1} on {domain.name}"
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
                      { "path": "domain.fc.originShare", "op": ">=", "value": 0.25 }
                    ]
                  },
                  "message": "Origin {domain.fc.originShare:p0}"
                }
              ]
            }
            """;

        HintRuleCompileBatchResult compiled = _compiler.CompileFile("t.json", json);
        compiled.Success.Should().BeTrue();
        IHintRule rule = compiled.Rules[0];

        (_, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipe) =
            AdminStatsMath.BuildAll(
                ocHits: 20, ocMisses: 30, ocBypass: 0,
                fcHits: 15, fcMisses: 15, fcStale: 0, fcBypass: 0,
                factoryRuns: 15, factoryFailures: 0);

        var ctx = new HintEvaluationContext
        {
            NowUtc = DateTimeOffset.UtcNow,
            Domain = new AdminDomainStatsDto
            {
                Name = "maps",
                Version = "1",
                Requests = 50,
                Oc = oc,
                Fc = fc,
                Pipeline = pipe
            }
        };

        IReadOnlyList<AdminHintDto> hints = rule.Evaluate(ctx).ToList();
        hints.Should().ContainSingle(h => h.Code == "team-origin" && h.Severity == "Warning");
        hints[0].Message.Should().Contain("%");
    }
}
