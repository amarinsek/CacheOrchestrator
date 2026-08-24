using System.Text.Json;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Optional package-owned overlay handler for domain settings not mapped by Core
/// (e.g. <c>fusionCache.*</c> from the FusionCache package).
/// </summary>
public interface IDomainSettingsPatchContributor
{
    /// <summary>Returns <see langword="true"/> when this contributor owns <paramref name="settingId"/>.</summary>
    bool Owns(string settingId);

    /// <summary>
    /// Applies a batch of owned overlay settings for <paramref name="domain"/>.
    /// Only keys for which <see cref="Owns"/> is true should be present.
    /// </summary>
    void Apply(string domain, IReadOnlyDictionary<string, JsonElement> settings);
}
