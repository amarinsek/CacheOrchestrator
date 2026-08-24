using System.Globalization;
using System.Text.Json;
using CacheOrchestrator.Admin;

namespace CacheOrchestrator.FusionCache;

/// <summary>Maps <c>fusionCache.*</c> Admin overlay keys onto <see cref="IFusionDomainRuntimeOverrideStore"/>.</summary>
public static class FusionSettingsPatchMapper
{
    /// <summary>Applies owned Fusion overlay keys for <paramref name="domain"/>.</summary>
    public static void Apply(
        string domain,
        IReadOnlyDictionary<string, JsonElement> settings,
        IFusionDomainRuntimeOverrideStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        if (settings.Count == 0)
            return;

        TimeSpan? hardTtl = null;
        TimeSpan? failSafe = null;
        double? eagerRefreshRatio = null;
        TimeSpan? jitter = null;
        TimeSpan? factorySoftTimeout = null;
        TimeSpan? factoryHardTimeout = null;
        int? maxItemBytes = null;
        bool? allowBackgroundDistributed = null;
        bool? allowBackgroundBackplane = null;

        foreach ((string rawKey, JsonElement el) in settings)
        {
            string id = rawKey;
            if (!id.StartsWith("fusionCache.", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Setting '{rawKey}' is not a Fusion overlay key.", nameof(settings));

            string suffix = id["fusionCache.".Length..];
            switch (suffix)
            {
                case "hardTtl": hardTtl = ReadNonNegTimeSpan(el, id); break;
                case "failSafe": failSafe = ReadNonNegTimeSpan(el, id); break;
                case "eagerRefreshRatio":
                    eagerRefreshRatio = ReadDouble(el, id);
                    if (eagerRefreshRatio is < 0 or >= 1)
                        throw new ArgumentException($"Setting '{id}' must be in [0, 1).", id);
                    break;
                case "jitter": jitter = ReadNonNegTimeSpan(el, id); break;
                case "factorySoftTimeout": factorySoftTimeout = ReadNonNegTimeSpan(el, id); break;
                case "factoryHardTimeout": factoryHardTimeout = ReadNonNegTimeSpan(el, id); break;
                case "maxItemBytes": maxItemBytes = ReadNonNegInt(el, id); break;
                case "allowBackgroundDistributed": allowBackgroundDistributed = ReadBool(el, id); break;
                case "allowBackgroundBackplane": allowBackgroundBackplane = ReadBool(el, id); break;
                default:
                    throw new ArgumentException($"Setting '{id}' is not mapped for Fusion overlay.", nameof(settings));
            }
        }

        FusionDomainSettingsPatch patch = new()
        {
            HardTtl = hardTtl,
            FailSafe = failSafe,
            EagerRefreshRatio = eagerRefreshRatio,
            Jitter = jitter,
            FactorySoftTimeout = factorySoftTimeout,
            FactoryHardTimeout = factoryHardTimeout,
            MaxItemBytes = maxItemBytes,
            AllowBackgroundDistributed = allowBackgroundDistributed,
            AllowBackgroundBackplane = allowBackgroundBackplane,
        };

        if (!patch.HasAny)
            throw new ArgumentException("At least one Fusion setting must be set.", nameof(settings));

        store.PatchSettings(domain, patch);
    }

    private static bool ReadBool(JsonElement el, string id) =>
        el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out bool b) => b,
            _ => throw new ArgumentException($"Setting '{id}' must be a boolean.", id),
        };

    private static int ReadNonNegInt(JsonElement el, string id)
    {
        int v = el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out int n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) => n,
            _ => throw new ArgumentException($"Setting '{id}' must be an integer.", id),
        };
        if (v < 0)
            throw new ArgumentException($"Setting '{id}' must be >= 0.", id);
        return v;
    }

    private static TimeSpan ReadNonNegTimeSpan(JsonElement el, string id)
    {
        TimeSpan v = el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out double seconds) => TimeSpan.FromSeconds(seconds),
            JsonValueKind.String when TryParseTimeSpan(el.GetString(), out TimeSpan parsed) => parsed,
            _ => throw new ArgumentException($"Setting '{id}' must be a TimeSpan string or total seconds number.", id),
        };
        if (v < TimeSpan.Zero)
            throw new ArgumentException($"Setting '{id}' must be >= 0.", id);
        return v;
    }

    private static bool TryParseTimeSpan(string? raw, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out value))
            return true;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            value = TimeSpan.FromSeconds(seconds);
            return true;
        }

        return false;
    }

    private static double ReadDouble(JsonElement el, string id) =>
        el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String when double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => d,
            _ => throw new ArgumentException($"Setting '{id}' must be a number.", id),
        };
}

/// <summary>Routes <c>fusionCache.*</c> Admin/cluster settings patches to the Fusion overlay store.</summary>
internal sealed class FusionDomainSettingsPatchContributor : IDomainSettingsPatchContributor
{
    private readonly IFusionDomainRuntimeOverrideStore _store;

    public FusionDomainSettingsPatchContributor(IFusionDomainRuntimeOverrideStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public bool Owns(string settingId) =>
        settingId.StartsWith("fusionCache.", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Apply(string domain, IReadOnlyDictionary<string, JsonElement> settings) =>
        FusionSettingsPatchMapper.Apply(domain, settings, _store);
}
