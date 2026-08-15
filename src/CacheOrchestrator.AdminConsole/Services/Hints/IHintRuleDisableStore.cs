namespace CacheOrchestrator.AdminConsole.Services.Hints;

/// <summary>Runtime + config disabled hint codes.</summary>
public interface IHintRuleDisableStore
{
    /// <summary>True when this code must not produce hints.</summary>
    bool IsDisabled(string code);

    /// <summary>All currently disabled codes (config ∪ local file).</summary>
    IReadOnlyCollection<string> GetDisabledCodes();

    /// <summary>Enable or disable a code; persists to the local overrides file.</summary>
    Task SetEnabledAsync(string code, bool enabled, CancellationToken cancellationToken = default);
}
