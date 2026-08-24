namespace CacheOrchestrator.Configuration;

/// <summary>One domain setting from <see cref="DomainSettingCatalog"/>.</summary>
public sealed class DomainSettingCatalogEntry
{
    /// <summary>Camel-case JSON / wire id (matches config property name casing for System.Text.Json).</summary>
    public required string Id { get; init; }

    /// <summary>CLR property name on <see cref="CacheOrchestratorOptions.DomainCacheSettings"/>.</summary>
    public required string PropertyName { get; init; }

    /// <summary>Human label for Admin UI.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Optional group.</summary>
    public string? Group { get; init; }

    /// <summary>Value kind.</summary>
    public DomainSettingValueKind Kind { get; init; }

    /// <summary>Whether runtime overlay patch is supported.</summary>
    public bool RuntimeOverlay { get; init; }

    /// <summary>Enum member names when <see cref="Kind"/> is <see cref="DomainSettingValueKind.Enum"/>.</summary>
    public IReadOnlyList<string>? EnumValues { get; init; }
}
