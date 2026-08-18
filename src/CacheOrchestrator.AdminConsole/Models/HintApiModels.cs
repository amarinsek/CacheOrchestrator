using CacheOrchestrator.AdminConsole.Services.Hints;

namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>Body for <c>PUT /api/hints/rules/{code}/enabled</c>.</summary>
public sealed class HintRuleEnableRequest
{
    public bool Enabled { get; set; } = true;
}

/// <summary>Response for <c>GET /api/hints/rules</c> (Settings catalog).</summary>
public sealed class HintRulesResponseDto
{
    /// <summary>Last pack load / compile status.</summary>
    public required HintRuleLoadStatus Load { get; init; }

    /// <summary>Enabled/disabled catalog rows (core + operator packs).</summary>
    public required IReadOnlyList<HintRuleCatalogEntry> Rules { get; init; }

    /// <summary>Documented declarative path catalog for operators.</summary>
    public required IReadOnlyList<string> KnownPaths { get; init; }
}
