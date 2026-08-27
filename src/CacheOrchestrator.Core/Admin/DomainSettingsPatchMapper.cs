using CacheOrchestrator.Configuration;
using System.Globalization;
using System.Text.Json;

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
    /// <see cref="ArgumentException"/>. Package-owned keys (e.g. <c>fusionCache.*</c>) must be
    /// routed via <see cref="DomainSettingsPatchApplicator"/> / <see cref="IDomainSettingsPatchContributor"/>.
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
        bool? dataCacheRespectAuthBypass = null;
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
        bool? dataCacheRespectNoStore = null;
        bool? dataCacheVaryOnPublicAddress = null;
        bool? dataCacheVaryOnEncoding = null;
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
                case "outputCache.enabled":
                    outputCacheEnabled = ReadBool(el, id);
                    break;
                case "dataCache.enabled":
                    dataCacheEnabled = ReadBool(el, id);
                    break;
                case "authBypassMode":
                    authBypassMode = ReadEnum<AuthBypassMode>(el, id);
                    break;
                case "varyOutputCacheByUser":
                    varyOutputCacheByUser = ReadBool(el, id);
                    break;
                case "treatAuthorizationAsAuthSignal":
                    treatAuthorizationAsAuthSignal = ReadBool(el, id);
                    break;
                case "authVaryIncludeAuthorizationHash":
                    authVaryIncludeAuthorizationHash = ReadBool(el, id);
                    break;
                case "dataCacheRespectAuthBypass":
                    dataCacheRespectAuthBypass = ReadBool(el, id);
                    break;
                case "clientCache.forcePrivateWhenAuthenticated":
                    clientForcePrivateWhenAuthenticated = ReadBool(el, id);
                    break;
                case "varyByAccept":
                    varyByAccept = ReadBool(el, id);
                    break;
                case "varyByAcceptLanguage":
                    varyByAcceptLanguage = ReadBool(el, id);
                    break;
                case "emitResponseVary":
                    emitResponseVary = ReadBool(el, id);
                    break;
                case "acceptNormalizationList":
                    acceptNormalizationList = ReadStringArray(el, id, max: 16);
                    break;
                case "acceptLanguageNormalizationList":
                    acceptLanguageNormalizationList = ReadStringArray(el, id, max: 16);
                    break;
                case "varyByHeaders":
                    varyByHeaders = ReadStringArray(el, id, max: MaxVaryByHeaders);
                    break;
                case "varyByQueryKeys":
                    varyByQueryKeys = ReadStringArray(el, id, max: 32);
                    break;
                case "ignoreQueryKeys":
                    ignoreQueryKeys = ReadStringArray(el, id, max: 32);
                    break;
                case "varyByCookies":
                    varyByCookies = ReadStringArray(el, id, max: MaxVaryByCookies);
                    break;
                case "varyByAuthClaims":
                    varyByAuthClaims = ReadStringArray(el, id, max: 16);
                    break;
                case "outputCache.eTagMode":
                    eTagMode = ReadEnum<ETagMode>(el, id);
                    break;
                case "clientCache.cacheability":
                    clientCacheability = ReadEnum<ClientCacheability>(el, id);
                    break;
                case "clientCache.ttlSeconds":
                    clientTtl = ReadNonNegSecondsAsTimeSpan(el, id);
                    break;
                case "clientCache.ttlMinSeconds":
                    clientTtlMin = ReadNonNegSecondsAsTimeSpan(el, id);
                    break;
                case "clientCache.scheduledUpdateUtc":
                    scheduledUpdateUtc = ReadDateTimeOffset(el, id);
                    break;
                case "clientCache.mustRevalidateNearUpdate":
                    clientMustRevalidateNearUpdate = ReadBool(el, id);
                    break;
                case "outputCache.ttlSeconds":
                    outputCacheTtl = ReadNonNegSecondsAsTimeSpan(el, id);
                    break;
                case "dataCache.ttlSeconds":
                    dataCacheTtl = ReadNonNegSecondsAsTimeSpan(el, id);
                    break;
                case "dataCache.respectNoStore":
                    dataCacheRespectNoStore = ReadBool(el, id);
                    break;
                case "dataCache.varyOnPublicAddress":
                    dataCacheVaryOnPublicAddress = ReadBool(el, id);
                    break;
                case "dataCache.varyOnEncoding":
                    dataCacheVaryOnEncoding = ReadBool(el, id);
                    break;
                case "outputCache.varyByHost":
                    outputCacheVaryByHost = ReadBool(el, id);
                    break;
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
            DataCacheRespectAuthBypass = dataCacheRespectAuthBypass,
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
            DataCacheRespectNoStore = dataCacheRespectNoStore,
            DataCacheVaryOnPublicAddress = dataCacheVaryOnPublicAddress,
            DataCacheVaryOnEncoding = dataCacheVaryOnEncoding,
            OutputCacheVaryByHost = outputCacheVaryByHost,
        };
    }

    private static bool ReadBool(JsonElement el, string id) =>
        el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out bool b) => b,
            _ => throw new ArgumentException($"Setting '{id}' must be a boolean.", id),
        };

    private static TimeSpan ReadNonNegSecondsAsTimeSpan(JsonElement el, string id)
    {
        int seconds = el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out int n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) => n,
            _ => throw new ArgumentException($"Setting '{id}' must be an integer number of seconds.", id),
        };
        if (seconds < 0)
            throw new ArgumentException($"Setting '{id}' must be >= 0.", id);
        return TimeSpan.FromSeconds(seconds);
    }

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
