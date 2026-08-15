namespace CacheOrchestrator.Admin.App.Options;

/// <summary>
/// Operator hint rules configuration. Bound from <c>CacheAdmin:Hints</c>.
/// </summary>
public sealed class HintOptions
{
    /// <summary>
    /// Glob or relative paths (from content root) to declarative rule JSON files.
    /// Example: <c>hints/*.json</c>, <c>hints/team-a.json</c>.
    /// </summary>
    public List<string> RuleFiles { get; set; } = [];

    /// <summary>
    /// Hint codes that never fire (built-in or declarative). Overridden at runtime by the Settings UI file.
    /// </summary>
    public List<string> DisabledCodes { get; set; } = [];

    /// <summary>
    /// Relative path (content root) for runtime enable/disable overrides written by the Settings UI.
    /// Default: <c>hints/disabled.local.json</c>.
    /// </summary>
    public string DisabledStatePath { get; set; } = "hints/disabled.local.json";
}
