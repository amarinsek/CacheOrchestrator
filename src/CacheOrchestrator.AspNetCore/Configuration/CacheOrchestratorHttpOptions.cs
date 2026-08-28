namespace CacheOrchestrator.Configuration;

/// <summary>ASP.NET Core domain policy bound from the same root Cache section as Core options.</summary>
internal sealed class CacheOrchestratorHttpOptions
{
    /// <summary>Global HTTP defaults applied to every domain.</summary>
    public DomainHttpCacheSettings DomainDefaults { get; set; } = new();

    /// <summary>Per-domain HTTP overrides.</summary>
    public Dictionary<string, DomainHttpCacheSettings> Domains { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>HTTP-specific policy for a domain or the global defaults.</summary>
internal sealed class DomainHttpCacheSettings
{
    /// <summary>HTTP behavior layered onto the portable Data Cache policy.</summary>
    public DomainHttpDataCacheSettings? DataCache { get; set; }

    /// <summary>Output Cache policy.</summary>
    public DomainOutputCacheSettings? OutputCache { get; set; }

    /// <summary>Client Cache policy.</summary>
    public DomainClientCacheSettings? ClientCache { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Enum, RuntimeOverlay = true, Group = "Cache", DisplayName = "Auth bypass mode")]
    public AuthBypassMode? AuthBypassMode { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Vary Output Cache by user")]
    public bool? VaryOutputCacheByUser { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Treat Authorization as auth signal")]
    public bool? TreatAuthorizationAsAuthSignal { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Auth vary include Authorization hash")]
    public bool? AuthVaryIncludeAuthorizationHash { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Cache", DisplayName = "Vary by auth claims")]
    public string[]? VaryByAuthClaims { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Data cache respects auth bypass")]
    public bool? DataCacheRespectAuthBypass { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by Accept")]
    public bool? VaryByAccept { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Accept normalization")]
    public string[]? AcceptNormalizationList { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by Accept-Language")]
    public bool? VaryByAcceptLanguage { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Accept-Language normalization")]
    public string[]? AcceptLanguageNormalizationList { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by headers")]
    public string[]? VaryByHeaders { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by query keys")]
    public string[]? VaryByQueryKeys { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Ignore query keys")]
    public string[]? IgnoreQueryKeys { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by cookies")]
    public string[]? VaryByCookies { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Vary", DisplayName = "Emit response Vary")]
    public bool? EmitResponseVary { get; set; }
}

/// <summary>HTTP request behavior for Data Cache.</summary>
public sealed class DomainHttpDataCacheSettings
{
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Respect no-store")]
    public bool? RespectNoStore { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Vary on public address")]
    public bool? VaryOnPublicAddress { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Vary on encoding")]
    public bool? VaryOnEncoding { get; set; }
}

/// <summary>Output Cache policy bound from <c>OutputCache</c> under a domain.</summary>
public sealed class DomainOutputCacheSettings
{
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Output Cache enabled")]
    public bool? Enabled { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Int, RuntimeOverlay = true, Group = "TTL", DisplayName = "Output Cache TTL (seconds)")]
    public int? TtlSeconds { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Vary Output Cache by host")]
    public bool? VaryByHost { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.IntArray, RuntimeOverlay = false, Group = "Cache", DisplayName = "Cacheable status codes")]
    public int[]? CacheableStatusCodes { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = false, Group = "Cache", DisplayName = "Encoding normalization")]
    public string[]? EncodingNormalizationList { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Enum, RuntimeOverlay = true, Group = "Cache", DisplayName = "ETag mode")]
    public ETagMode? ETagMode { get; set; }
}

/// <summary>Client Cache policy bound from <c>ClientCache</c> under a domain.</summary>
public sealed class DomainClientCacheSettings
{
    [DomainSetting(Kind = DomainSettingValueKind.Enum, RuntimeOverlay = true, Group = "Client", DisplayName = "Client cacheability")]
    public ClientCacheability? Cacheability { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Int, RuntimeOverlay = true, Group = "Client", DisplayName = "Client TTL (seconds)")]
    public int? TtlSeconds { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Int, RuntimeOverlay = true, Group = "Client", DisplayName = "Client TTL min (seconds)")]
    public int? TtlMinSeconds { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.DateTimeOffset, RuntimeOverlay = true, Group = "Client", DisplayName = "Scheduled update (UTC)")]
    public DateTimeOffset? ScheduledUpdateUtc { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Client", DisplayName = "Must-revalidate near update")]
    public bool? MustRevalidateNearUpdate { get; set; }

    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Client", DisplayName = "Force private when authenticated")]
    public bool? ForcePrivateWhenAuthenticated { get; set; }
}
