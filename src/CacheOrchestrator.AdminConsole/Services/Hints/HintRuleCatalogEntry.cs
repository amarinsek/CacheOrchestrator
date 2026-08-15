namespace CacheOrchestrator.AdminConsole.Services.Hints;

/// <summary>One row in the Settings / API rule catalog.</summary>
public sealed class HintRuleCatalogEntry
{
    public required string Id { get; init; }
    public required string Code { get; init; }
    public string? Category { get; init; }
    public required string Scope { get; init; }
    public required string Source { get; init; }
    public string? Description { get; init; }
    public string? DefaultSeverity { get; init; }
    public required bool Enabled { get; init; }
    public required bool IsBuiltIn { get; init; }
    public IReadOnlyList<string> EmittedCodes { get; init; } = [];

    /// <summary>Original rule JSON (pretty), when available from a declarative pack.</summary>
    public string? DefinitionJson { get; init; }
}

/// <summary>Load / compile diagnostics for declarative files.</summary>
public sealed class HintRuleLoadStatus
{
    public required bool Ok { get; init; }
    public required int RuleCount { get; init; }
    public required int FileCount { get; init; }
    public IReadOnlyList<HintRuleCompileError> Errors { get; init; } = [];
    public DateTimeOffset LoadedAtUtc { get; init; }
}

/// <summary>Compiler / checker error for a declarative rule file.</summary>
public sealed record HintRuleCompileError
{
    public required string File { get; init; }

    /// <summary>Rule <c>code</c> when known (even if other fields failed validation).</summary>
    public string? RuleCode { get; init; }

    /// <summary>
    /// Path inside the rule object when <see cref="RuleCode"/> is known (e.g. <c>when.all[0].op</c>),
    /// otherwise document path (e.g. <c>rules[10].when.all[0].op</c>).
    /// </summary>
    public required string Path { get; init; }

    public required string Message { get; init; }
}
