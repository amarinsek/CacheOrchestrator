namespace CacheOrchestrator.Configuration;

/// <summary>
/// Marks a <see cref="CacheOrchestratorOptions.DomainCacheSettings"/> property as a documented
/// domain setting for Admin catalog / Operations patch UI.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class DomainSettingAttribute : Attribute
{
    /// <summary>Value kind for UI controls and validation.</summary>
    public DomainSettingValueKind Kind { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the setting may be applied via process-local runtime overlay
    /// (<c>PATCH …/settings</c>). When <see langword="false"/>, catalog-only (config / future).
    /// </summary>
    public bool RuntimeOverlay { get; init; }

    /// <summary>Optional UI group (e.g. <c>TTL</c>, <c>Fusion</c>, <c>Client</c>).</summary>
    public string? Group { get; init; }

    /// <summary>Optional display label; default is the property name split for readability.</summary>
    public string? DisplayName { get; init; }
}
