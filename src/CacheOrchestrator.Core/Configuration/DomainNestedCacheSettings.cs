namespace CacheOrchestrator.Configuration;

/// <summary>Portable Data Cache policy (Fusion / Hybrid). Bound from <c>DataCache</c>.</summary>
public sealed class DomainDataCacheSettings
{
    /// <summary>Enable data cache for this domain.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Data Cache enabled")]
    public bool? Enabled { get; set; }

    /// <summary>
    /// Named data-cache instance (today: key in <see cref="CacheOrchestratorOptions.DataCacheInstances"/>).
    /// </summary>
    [DomainSetting(Kind = DomainSettingValueKind.String, RuntimeOverlay = false, Group = "Data", DisplayName = "Data Cache instance")]
    public string? Instance { get; set; }

    /// <summary>Logical data-cache TTL (maps to Fusion soft duration / Hybrid expiration).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "TTL", DisplayName = "Data Cache TTL")]
    public TimeSpan? Ttl { get; set; }

    /// <summary>When true, skip data cache if the request has Cache-Control: no-store.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Respect no-store")]
    public bool? RespectNoStore { get; set; }

    /// <summary>Include scheme/host in the data-cache key.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Vary on public address")]
    public bool? VaryOnPublicAddress { get; set; }

    /// <summary>Include Accept-Encoding in the data-cache key.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Vary on encoding")]
    public bool? VaryOnEncoding { get; set; }

    /// <summary>Hard (absolute) duration cap (Fusion; ignored by Hybrid).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "TTL", DisplayName = "Data Cache hard TTL")]
    public TimeSpan? HardTtl { get; set; }

    /// <summary>Fail-safe max duration (Fusion; ignored by Hybrid).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "TTL", DisplayName = "Data Cache fail-safe")]
    public TimeSpan? FailSafe { get; set; }

    /// <summary>Eager refresh threshold ratio (0–1 exclusive). 0 = disabled (Fusion; ignored by Hybrid).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Double, RuntimeOverlay = true, Group = "Data", DisplayName = "Eager refresh ratio")]
    public double? EagerRefreshRatio { get; set; }

    /// <summary>Max jitter added to data-cache duration (Fusion; ignored by Hybrid).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "Data", DisplayName = "Data Cache jitter")]
    public TimeSpan? Jitter { get; set; }

    /// <summary>Factory soft timeout (Fusion; ignored by Hybrid).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "Data", DisplayName = "Factory soft timeout")]
    public TimeSpan? FactorySoftTimeout { get; set; }

    /// <summary>Factory hard timeout (Fusion; ignored by Hybrid).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "Data", DisplayName = "Factory hard timeout")]
    public TimeSpan? FactoryHardTimeout { get; set; }

    /// <summary>Optional max item size for memory cache (bytes). 0 = unlimited (Fusion; ignored by Hybrid).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Int, RuntimeOverlay = true, Group = "Data", DisplayName = "Max item bytes")]
    public int? MaxItemBytes { get; set; }

    /// <summary>Allow background distributed cache operations (Fusion; ignored by Hybrid).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Background distributed ops")]
    public bool? AllowBackgroundDistributed { get; set; }

    /// <summary>Allow background backplane operations (Fusion; ignored by Hybrid).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Background backplane ops")]
    public bool? AllowBackgroundBackplane { get; set; }
}

/// <summary>Output Cache policy. Bound from <c>OutputCache</c> under a domain.</summary>
public sealed class DomainOutputCacheSettings
{
    /// <summary>Enable Output Cache for this domain.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Output Cache enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Output Cache entry TTL.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "TTL", DisplayName = "Output Cache TTL")]
    public TimeSpan? Ttl { get; set; }

    /// <summary>When true (default), Output Cache varies by host (includes port).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Vary Output Cache by host")]
    public bool? VaryByHost { get; set; }

    /// <summary>HTTP status codes that may be stored in Output Cache.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.IntArray, RuntimeOverlay = false, Group = "Cache", DisplayName = "Cacheable status codes")]
    public int[]? CacheableStatusCodes { get; set; }

    /// <summary>Preferred Accept-Encoding values for normalization.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = false, Group = "Cache", DisplayName = "Encoding normalization")]
    public string[]? EncodingNormalizationList { get; set; }

    /// <summary>How the Output Cache policy sets the HTTP ETag header.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Enum, RuntimeOverlay = true, Group = "Cache", DisplayName = "ETag mode")]
    public ETagMode? ETagMode { get; set; }
}

/// <summary>Client Cache-Control policy. Bound from <c>ClientCache</c>.</summary>
public sealed class DomainClientCacheSettings
{
    /// <summary>Client cache mode. Default: Public.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Enum, RuntimeOverlay = true, Group = "Client", DisplayName = "Client cacheability")]
    public ClientCacheability? Cacheability { get; set; }

    /// <summary>Desired max-age far from update.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "Client", DisplayName = "Client TTL")]
    public TimeSpan? Ttl { get; set; }

    /// <summary>Floor max-age near/at update.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "Client", DisplayName = "Client TTL min")]
    public TimeSpan? TtlMin { get; set; }

    /// <summary>Next planned content cutover (UTC). Null = always use <see cref="Ttl"/>.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.DateTimeOffset, RuntimeOverlay = true, Group = "Client", DisplayName = "Scheduled update (UTC)")]
    public DateTimeOffset? ScheduledUpdateUtc { get; set; }

    /// <summary>Append must-revalidate when max-age is at or below min.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Client", DisplayName = "Must-revalidate near update")]
    public bool? MustRevalidateNearUpdate { get; set; }

    /// <summary>Force client Private when Identity is authenticated and cacheability is Public.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Client", DisplayName = "Force private when authenticated")]
    public bool? ForcePrivateWhenAuthenticated { get; set; }
}
