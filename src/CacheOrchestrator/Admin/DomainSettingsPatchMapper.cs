using System.Globalization;
using System.Text.Json;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Vary;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Maps a sparse camelCase settings dictionary (Admin JSON) onto <see cref="DomainSettingsPatch"/>.
/// </summary>
public static class DomainSettingsPatchMapper
{
    /// <summary>
    /// Builds a patch from wire values. Unknown keys or non-overlay settings throw
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public static DomainSettingsPatch FromDictionary(IReadOnlyDictionary<string, JsonElement> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count == 0)
            throw new ArgumentException("At least one setting must be set.", nameof(settings));

        bool? outputCacheEnabled = null;
        bool? fusionCacheEnabled = null;
        bool? bypassWhenAuthenticated = null;
        AuthBypassMode? authBypassMode = null;
        bool? varyOutputCacheByUser = null;
        bool? treatAuthorizationAsAuthSignal = null;
        bool? authVaryIncludeAuthorizationHash = null;
        bool? fusionRespectAuthBypass = null;
        bool? clientForcePrivateWhenAuthenticated = null;
        bool? varyByAccept = null;
        bool? varyByAcceptLanguage = null;
        bool? emitResponseVary = null;
        string[]? acceptNormalizationList = null;
        string[]? acceptLanguageNormalizationList = null;
        string[]? varyByHeaders = null;
        string[]? varyByQueryKeys = null;
        string[]? ignoreQueryKeys = null;
        string[]? varyByCookies = null;
        string[]? varyByAuthClaims = null;
        ETagMode? eTagMode = null;
        ClientCacheability? clientCacheability = null;
        int? clientTtlSeconds = null;
        int? clientTtlMinSeconds = null;
        DateTimeOffset? scheduledUpdateUtc = null;
        bool? clientMustRevalidateNearUpdate = null;
        int? outputCacheTtlSeconds = null;
        int? fusionCacheSoftTtlSeconds = null;
        int? fusionCacheHardTtlSeconds = null;
        int? fusionCacheFailSafeSeconds = null;
        double? fusionCacheEagerRefreshRatio = null;
        int? fusionCacheJitterSeconds = null;
        int? fusionCacheFactorySoftTimeoutSeconds = null;
        int? fusionCacheFactoryHardTimeoutSeconds = null;
        int? fusionCacheMaxItemBytes = null;
        bool? fusionCacheRespectNoStore = null;
        bool? fusionCacheAllowBackgroundDistributed = null;
        bool? fusionCacheAllowBackgroundBackplane = null;
        bool? fusionCacheVaryOnPublicAddress = null;
        bool? fusionCacheVaryOnEncoding = null;
        bool? outputCacheVaryByHost = null;

        foreach ((string rawKey, JsonElement el) in settings)
        {
            DomainSettingCatalogEntry? entry = DomainSettingCatalog.Find(rawKey)
                ?? throw new ArgumentException($"Unknown domain setting '{rawKey}'.", nameof(settings));
            if (!entry.RuntimeOverlay)
                throw new ArgumentException($"Setting '{entry.Id}' is not runtime-patchable.", nameof(settings));

            string id = entry.Id;
            switch (id)
            {
                case "outputCacheEnabled": outputCacheEnabled = ReadBool(el, id); break;
                case "fusionCacheEnabled": fusionCacheEnabled = ReadBool(el, id); break;
                case "bypassWhenAuthenticated": bypassWhenAuthenticated = ReadBool(el, id); break;
                case "authBypassMode": authBypassMode = ReadEnum<AuthBypassMode>(el, id); break;
                case "varyOutputCacheByUser": varyOutputCacheByUser = ReadBool(el, id); break;
                case "treatAuthorizationAsAuthSignal": treatAuthorizationAsAuthSignal = ReadBool(el, id); break;
                case "authVaryIncludeAuthorizationHash": authVaryIncludeAuthorizationHash = ReadBool(el, id); break;
                case "fusionRespectAuthBypass": fusionRespectAuthBypass = ReadBool(el, id); break;
                case "clientForcePrivateWhenAuthenticated": clientForcePrivateWhenAuthenticated = ReadBool(el, id); break;
                case "varyByAccept": varyByAccept = ReadBool(el, id); break;
                case "varyByAcceptLanguage": varyByAcceptLanguage = ReadBool(el, id); break;
                case "emitResponseVary": emitResponseVary = ReadBool(el, id); break;
                case "acceptNormalizationList": acceptNormalizationList = ReadStringArray(el, id, max: 16); break;
                case "acceptLanguageNormalizationList": acceptLanguageNormalizationList = ReadStringArray(el, id, max: 16); break;
                case "varyByHeaders": varyByHeaders = ReadStringArray(el, id, max: CacheVaryMaterializer.MaxVaryByHeaders); break;
                case "varyByQueryKeys": varyByQueryKeys = ReadStringArray(el, id, max: 32); break;
                case "ignoreQueryKeys": ignoreQueryKeys = ReadStringArray(el, id, max: 32); break;
                case "varyByCookies": varyByCookies = ReadStringArray(el, id, max: CacheVaryMaterializer.MaxVaryByCookies); break;
                case "varyByAuthClaims": varyByAuthClaims = ReadStringArray(el, id, max: 16); break;
                case "eTagMode": eTagMode = ReadEnum<ETagMode>(el, id); break;
                case "clientCacheability": clientCacheability = ReadEnum<ClientCacheability>(el, id); break;
                case "clientTtlSeconds": clientTtlSeconds = ReadNonNegInt(el, id); break;
                case "clientTtlMinSeconds": clientTtlMinSeconds = ReadNonNegInt(el, id); break;
                case "scheduledUpdateUtc": scheduledUpdateUtc = ReadDateTimeOffset(el, id); break;
                case "clientMustRevalidateNearUpdate": clientMustRevalidateNearUpdate = ReadBool(el, id); break;
                case "outputCacheTtlSeconds": outputCacheTtlSeconds = ReadNonNegInt(el, id); break;
                case "fusionCacheSoftTtlSeconds": fusionCacheSoftTtlSeconds = ReadNonNegInt(el, id); break;
                case "fusionCacheHardTtlSeconds": fusionCacheHardTtlSeconds = ReadNonNegInt(el, id); break;
                case "fusionCacheFailSafeSeconds": fusionCacheFailSafeSeconds = ReadNonNegInt(el, id); break;
                case "fusionCacheEagerRefreshRatio": fusionCacheEagerRefreshRatio = ReadDouble(el, id); break;
                case "fusionCacheJitterSeconds": fusionCacheJitterSeconds = ReadNonNegInt(el, id); break;
                case "fusionCacheFactorySoftTimeoutSeconds": fusionCacheFactorySoftTimeoutSeconds = ReadNonNegInt(el, id); break;
                case "fusionCacheFactoryHardTimeoutSeconds": fusionCacheFactoryHardTimeoutSeconds = ReadNonNegInt(el, id); break;
                case "fusionCacheMaxItemBytes": fusionCacheMaxItemBytes = ReadNonNegInt(el, id); break;
                case "fusionCacheRespectNoStore": fusionCacheRespectNoStore = ReadBool(el, id); break;
                case "fusionCacheAllowBackgroundDistributed": fusionCacheAllowBackgroundDistributed = ReadBool(el, id); break;
                case "fusionCacheAllowBackgroundBackplane": fusionCacheAllowBackgroundBackplane = ReadBool(el, id); break;
                case "fusionCacheVaryOnPublicAddress": fusionCacheVaryOnPublicAddress = ReadBool(el, id); break;
                case "fusionCacheVaryOnEncoding": fusionCacheVaryOnEncoding = ReadBool(el, id); break;
                case "outputCacheVaryByHost": outputCacheVaryByHost = ReadBool(el, id); break;
                default:
                    throw new ArgumentException($"Setting '{id}' is not mapped for overlay.", nameof(settings));
            }
        }

        return new DomainSettingsPatch
        {
            OutputCacheEnabled = outputCacheEnabled,
            FusionCacheEnabled = fusionCacheEnabled,
            BypassWhenAuthenticated = bypassWhenAuthenticated,
            AuthBypassMode = authBypassMode,
            VaryOutputCacheByUser = varyOutputCacheByUser,
            TreatAuthorizationAsAuthSignal = treatAuthorizationAsAuthSignal,
            AuthVaryIncludeAuthorizationHash = authVaryIncludeAuthorizationHash,
            FusionRespectAuthBypass = fusionRespectAuthBypass,
            ClientForcePrivateWhenAuthenticated = clientForcePrivateWhenAuthenticated,
            VaryByAccept = varyByAccept,
            VaryByAcceptLanguage = varyByAcceptLanguage,
            EmitResponseVary = emitResponseVary,
            AcceptNormalizationList = acceptNormalizationList,
            AcceptLanguageNormalizationList = acceptLanguageNormalizationList,
            VaryByHeaders = varyByHeaders,
            VaryByQueryKeys = varyByQueryKeys,
            IgnoreQueryKeys = ignoreQueryKeys,
            VaryByCookies = varyByCookies,
            VaryByAuthClaims = varyByAuthClaims,
            ETagMode = eTagMode,
            ClientCacheability = clientCacheability,
            ClientTtlSeconds = clientTtlSeconds,
            ClientTtlMinSeconds = clientTtlMinSeconds,
            ScheduledUpdateUtc = scheduledUpdateUtc,
            ClientMustRevalidateNearUpdate = clientMustRevalidateNearUpdate,
            OutputCacheTtlSeconds = outputCacheTtlSeconds,
            FusionCacheSoftTtlSeconds = fusionCacheSoftTtlSeconds,
            FusionCacheHardTtlSeconds = fusionCacheHardTtlSeconds,
            FusionCacheFailSafeSeconds = fusionCacheFailSafeSeconds,
            FusionCacheEagerRefreshRatio = fusionCacheEagerRefreshRatio,
            FusionCacheJitterSeconds = fusionCacheJitterSeconds,
            FusionCacheFactorySoftTimeoutSeconds = fusionCacheFactorySoftTimeoutSeconds,
            FusionCacheFactoryHardTimeoutSeconds = fusionCacheFactoryHardTimeoutSeconds,
            FusionCacheMaxItemBytes = fusionCacheMaxItemBytes,
            FusionCacheRespectNoStore = fusionCacheRespectNoStore,
            FusionCacheAllowBackgroundDistributed = fusionCacheAllowBackgroundDistributed,
            FusionCacheAllowBackgroundBackplane = fusionCacheAllowBackgroundBackplane,
            FusionCacheVaryOnPublicAddress = fusionCacheVaryOnPublicAddress,
            FusionCacheVaryOnEncoding = fusionCacheVaryOnEncoding,
            OutputCacheVaryByHost = outputCacheVaryByHost,
        };
    }

    /// <summary>Maps a legacy TTL DTO onto <see cref="DomainSettingsPatch"/>.</summary>
    public static DomainSettingsPatch FromTtlRequest(AdminTtlPatchRequest body) =>
        new()
        {
            OutputCacheTtlSeconds = body.OutputCacheTtlSeconds,
            FusionCacheSoftTtlSeconds = body.FusionCacheSoftTtlSeconds,
            FusionCacheHardTtlSeconds = body.FusionCacheHardTtlSeconds,
            FusionCacheFailSafeSeconds = body.FusionCacheFailSafeSeconds,
            ClientTtlSeconds = body.ClientTtlSeconds,
            ClientTtlMinSeconds = body.ClientTtlMinSeconds,
        };

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

    private static double ReadDouble(JsonElement el, string id) =>
        el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String when double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => d,
            _ => throw new ArgumentException($"Setting '{id}' must be a number.", id),
        };

    private static DateTimeOffset ReadDateTimeOffset(JsonElement el, string id)
    {
        if (el.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(el.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset dto))
        {
            return dto.ToUniversalTime();
        }

        throw new ArgumentException($"Setting '{id}' must be an ISO-8601 date-time.", id);
    }

    private static TEnum ReadEnum<TEnum>(JsonElement el, string id) where TEnum : struct, Enum
    {
        if (el.ValueKind == JsonValueKind.String
            && Enum.TryParse(el.GetString(), ignoreCase: true, out TEnum v))
        {
            return v;
        }

        throw new ArgumentException($"Setting '{id}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.", id);
    }

    private static string[] ReadStringArray(JsonElement el, string id, int max)
    {
        if (el.ValueKind == JsonValueKind.Null)
            return [];

        if (el.ValueKind == JsonValueKind.String)
        {
            string? raw = el.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return [];
            string[] fromCsv = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return ValidateStringArray(fromCsv, id, max);
        }

        if (el.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"Setting '{id}' must be a JSON array of strings (or a comma-separated string).", id);

        List<string> list = new(el.GetArrayLength());
        foreach (JsonElement item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"Setting '{id}' array entries must be strings.", id);
            string? s = item.GetString();
            if (string.IsNullOrWhiteSpace(s))
                throw new ArgumentException($"Setting '{id}' entries must not be empty.", id);
            list.Add(s.Trim());
        }

        return ValidateStringArray(list.ToArray(), id, max);
    }

    private static string[] ValidateStringArray(string[] values, string id, int max)
    {
        if (values.Length > max)
            throw new ArgumentException($"Setting '{id}' cannot contain more than {max} entries.", id);
        return values;
    }
}
