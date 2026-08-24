using System.Globalization;
using System.Text.Json;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Maps a sparse camelCase settings dictionary (Admin JSON) onto <see cref="DomainSettingsPatch"/>.
/// </summary>
public static class DomainSettingsPatchMapper
{
    /// <summary>Maximum entries allowed in <c>VaryByHeaders</c> (aligned with AspNet vary materializer).</summary>
    public const int MaxVaryByHeaders = 8;

    /// <summary>Maximum entries allowed in <c>VaryByCookies</c> (aligned with AspNet vary materializer).</summary>
    public const int MaxVaryByCookies = 8;

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
        bool? dataCacheEnabled = null;
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
        TimeSpan? clientTtl = null;
        TimeSpan? clientTtlMin = null;
        DateTimeOffset? scheduledUpdateUtc = null;
        bool? clientMustRevalidateNearUpdate = null;
        TimeSpan? outputCacheTtl = null;
        TimeSpan? dataCacheTtl = null;
        TimeSpan? fusionCacheHardTtl = null;
        TimeSpan? fusionCacheFailSafe = null;
        double? fusionCacheEagerRefreshRatio = null;
        TimeSpan? fusionCacheJitter = null;
        TimeSpan? fusionCacheFactorySoftTimeout = null;
        TimeSpan? fusionCacheFactoryHardTimeout = null;
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
                case "outputCache.enabled": outputCacheEnabled = ReadBool(el, id); break;
                case "dataCache.enabled": dataCacheEnabled = ReadBool(el, id); break;
                case "authBypassMode": authBypassMode = ReadEnum<AuthBypassMode>(el, id); break;
                case "varyOutputCacheByUser": varyOutputCacheByUser = ReadBool(el, id); break;
                case "treatAuthorizationAsAuthSignal": treatAuthorizationAsAuthSignal = ReadBool(el, id); break;
                case "authVaryIncludeAuthorizationHash": authVaryIncludeAuthorizationHash = ReadBool(el, id); break;
                case "dataCacheRespectAuthBypass": fusionRespectAuthBypass = ReadBool(el, id); break;
                case "clientCache.forcePrivateWhenAuthenticated": clientForcePrivateWhenAuthenticated = ReadBool(el, id); break;
                case "varyByAccept": varyByAccept = ReadBool(el, id); break;
                case "varyByAcceptLanguage": varyByAcceptLanguage = ReadBool(el, id); break;
                case "emitResponseVary": emitResponseVary = ReadBool(el, id); break;
                case "acceptNormalizationList": acceptNormalizationList = ReadStringArray(el, id, max: 16); break;
                case "acceptLanguageNormalizationList": acceptLanguageNormalizationList = ReadStringArray(el, id, max: 16); break;
                case "varyByHeaders": varyByHeaders = ReadStringArray(el, id, max: MaxVaryByHeaders); break;
                case "varyByQueryKeys": varyByQueryKeys = ReadStringArray(el, id, max: 32); break;
                case "ignoreQueryKeys": ignoreQueryKeys = ReadStringArray(el, id, max: 32); break;
                case "varyByCookies": varyByCookies = ReadStringArray(el, id, max: MaxVaryByCookies); break;
                case "varyByAuthClaims": varyByAuthClaims = ReadStringArray(el, id, max: 16); break;
                case "outputCache.eTagMode": eTagMode = ReadEnum<ETagMode>(el, id); break;
                case "clientCache.cacheability": clientCacheability = ReadEnum<ClientCacheability>(el, id); break;
                case "clientCache.ttl": clientTtl = ReadNonNegTimeSpan(el, id); break;
                case "clientCache.ttlMin": clientTtlMin = ReadNonNegTimeSpan(el, id); break;
                case "clientCache.scheduledUpdateUtc": scheduledUpdateUtc = ReadDateTimeOffset(el, id); break;
                case "clientCache.mustRevalidateNearUpdate": clientMustRevalidateNearUpdate = ReadBool(el, id); break;
                case "outputCache.ttl": outputCacheTtl = ReadNonNegTimeSpan(el, id); break;
                case "dataCache.ttl": dataCacheTtl = ReadNonNegTimeSpan(el, id); break;
                case "fusionCache.hardTtl": fusionCacheHardTtl = ReadNonNegTimeSpan(el, id); break;
                case "fusionCache.failSafe": fusionCacheFailSafe = ReadNonNegTimeSpan(el, id); break;
                case "fusionCache.eagerRefreshRatio": fusionCacheEagerRefreshRatio = ReadDouble(el, id); break;
                case "fusionCache.jitter": fusionCacheJitter = ReadNonNegTimeSpan(el, id); break;
                case "fusionCache.factorySoftTimeout": fusionCacheFactorySoftTimeout = ReadNonNegTimeSpan(el, id); break;
                case "fusionCache.factoryHardTimeout": fusionCacheFactoryHardTimeout = ReadNonNegTimeSpan(el, id); break;
                case "fusionCache.maxItemBytes": fusionCacheMaxItemBytes = ReadNonNegInt(el, id); break;
                case "dataCache.respectNoStore": fusionCacheRespectNoStore = ReadBool(el, id); break;
                case "fusionCache.allowBackgroundDistributed": fusionCacheAllowBackgroundDistributed = ReadBool(el, id); break;
                case "fusionCache.allowBackgroundBackplane": fusionCacheAllowBackgroundBackplane = ReadBool(el, id); break;
                case "dataCache.varyOnPublicAddress": fusionCacheVaryOnPublicAddress = ReadBool(el, id); break;
                case "dataCache.varyOnEncoding": fusionCacheVaryOnEncoding = ReadBool(el, id); break;
                case "outputCache.varyByHost": outputCacheVaryByHost = ReadBool(el, id); break;
                default:
                    throw new ArgumentException($"Setting '{id}' is not mapped for overlay.", nameof(settings));
            }
        }

        return new DomainSettingsPatch
        {
            OutputCacheEnabled = outputCacheEnabled,
            DataCacheEnabled = dataCacheEnabled,
            AuthBypassMode = authBypassMode,
            VaryOutputCacheByUser = varyOutputCacheByUser,
            TreatAuthorizationAsAuthSignal = treatAuthorizationAsAuthSignal,
            AuthVaryIncludeAuthorizationHash = authVaryIncludeAuthorizationHash,
            DataCacheRespectAuthBypass = fusionRespectAuthBypass,
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
            ClientTtl = clientTtl,
            ClientTtlMin = clientTtlMin,
            ScheduledUpdateUtc = scheduledUpdateUtc,
            ClientMustRevalidateNearUpdate = clientMustRevalidateNearUpdate,
            OutputCacheTtl = outputCacheTtl,
            DataCacheTtl = dataCacheTtl,
            HardTtl = fusionCacheHardTtl,
            FailSafe = fusionCacheFailSafe,
            EagerRefreshRatio = fusionCacheEagerRefreshRatio,
            Jitter = fusionCacheJitter,
            FactorySoftTimeout = fusionCacheFactorySoftTimeout,
            FactoryHardTimeout = fusionCacheFactoryHardTimeout,
            MaxItemBytes = fusionCacheMaxItemBytes,
            DataCacheRespectNoStore = fusionCacheRespectNoStore,
            AllowBackgroundDistributed = fusionCacheAllowBackgroundDistributed,
            AllowBackgroundBackplane = fusionCacheAllowBackgroundBackplane,
            DataCacheVaryOnPublicAddress = fusionCacheVaryOnPublicAddress,
            DataCacheVaryOnEncoding = fusionCacheVaryOnEncoding,
            OutputCacheVaryByHost = outputCacheVaryByHost,
        };
    }

    /// <summary>Maps a legacy TTL DTO onto <see cref="DomainSettingsPatch"/>.</summary>
    public static DomainSettingsPatch FromTtlRequest(AdminTtlPatchRequest body) =>
        new()
        {
            OutputCacheTtl = FromSeconds(body.OutputCacheTtlSeconds),
            DataCacheTtl = FromSeconds(body.DataCacheTtlSeconds),
            HardTtl = FromSeconds(body.HardTtlSeconds),
            FailSafe = FromSeconds(body.FailSafeSeconds),
            ClientTtl = FromSeconds(body.ClientTtlSeconds),
            ClientTtlMin = FromSeconds(body.ClientTtlMinSeconds),
        };

    private static TimeSpan? FromSeconds(int? seconds) =>
        seconds is int s ? TimeSpan.FromSeconds(s) : null;

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
