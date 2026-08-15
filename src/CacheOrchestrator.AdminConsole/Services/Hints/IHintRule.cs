namespace CacheOrchestrator.AdminConsole.Services.Hints;

/// <summary>
/// One hint producer. Built-in packs and declarative JSON rules both implement this.
/// </summary>
public interface IHintRule
{
    /// <summary>Stable rule id (usually same as <see cref="Code"/> for single-code rules).</summary>
    string Id { get; }

    /// <summary>Primary machine-readable code emitted by this rule (catalog / disable key).</summary>
    string Code { get; }

    /// <summary>Optional grouping (Origin, Schedule, TTL, …).</summary>
    string? Category { get; }

    /// <summary><c>domain</c>, <c>endpoint</c>, or <c>any</c>.</summary>
    string Scope { get; }

    /// <summary>Where the rule was loaded from (e.g. <c>built-in</c>, <c>file:hints/custom.json</c>).</summary>
    string Source { get; }

    /// <summary>Human description for Settings catalog.</summary>
    string? Description { get; }

    /// <summary>Default severity when the rule fires (declarative). Built-in may emit multiple severities.</summary>
    string? DefaultSeverity { get; }

    /// <summary>
    /// Codes this rule may emit (for disable filtering). Built-in packs list every code they produce.
    /// </summary>
    IReadOnlyList<string> EmittedCodes { get; }

    /// <summary>Evaluate against the current entity context. Return zero or more hints.</summary>
    IEnumerable<CacheOrchestrator.Admin.AdminHintDto> Evaluate(HintEvaluationContext context);
}
