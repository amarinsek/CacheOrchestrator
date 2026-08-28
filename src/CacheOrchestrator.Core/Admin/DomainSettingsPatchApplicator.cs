using CacheOrchestrator.Configuration;
using System.Text.Json;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Applies a sparse Admin settings dictionary to Core overlays and optional package contributors.
/// </summary>
public static class DomainSettingsPatchApplicator
{
    /// <summary>
    /// Validates catalog ids, routes Core keys through <see cref="DomainSettingsPatchMapper"/>,
    /// and forwards owned extras to <paramref name="contributors"/>.
    /// </summary>
    public static DomainSettingsPatch Apply(
        string domain,
        IReadOnlyDictionary<string, JsonElement> settings,
        IDomainRuntimeOverrideStore store,
        IEnumerable<IDomainSettingsPatchContributor>? contributors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        if (settings.Count == 0)
            throw new ArgumentException("At least one setting must be set.", nameof(settings));

        IDomainSettingsPatchContributor[] contribs = contributors?.ToArray() ?? [];
        Dictionary<string, JsonElement> core = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<IDomainSettingsPatchContributor, Dictionary<string, JsonElement>> byContributor = new();

        foreach ((string rawKey, JsonElement el) in settings)
        {
            DomainSettingCatalogEntry entry = DomainSettingCatalog.Find(rawKey)
                ?? throw new ArgumentException($"Unknown domain setting '{rawKey}'.", nameof(settings));
            if (!entry.RuntimeOverlay)
                throw new ArgumentException($"Setting '{entry.Id}' is not runtime-patchable.", nameof(settings));

            IDomainSettingsPatchContributor? owner = contribs.FirstOrDefault(c => c.Owns(entry.Id));
            if (owner is not null)
            {
                if (!byContributor.TryGetValue(owner, out Dictionary<string, JsonElement>? bag))
                {
                    bag = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    byContributor[owner] = bag;
                }

                bag[entry.Id] = el;
                continue;
            }

            core[entry.Id] = el;
        }

        DomainSettingsPatch patch = new();
        if (core.Count > 0)
        {
            patch = DomainSettingsPatchMapper.FromDictionary(core);
            store.PatchSettings(domain, patch);
        }

        foreach ((IDomainSettingsPatchContributor contributor, Dictionary<string, JsonElement> bag) in byContributor)
            contributor.Apply(domain, bag);

        if (core.Count == 0 && byContributor.Count == 0)
            throw new ArgumentException("At least one setting must be set.", nameof(settings));

        return patch;
    }
}
