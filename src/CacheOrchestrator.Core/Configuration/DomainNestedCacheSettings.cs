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

    /// <summary>Logical data-cache TTL in seconds (maps to Fusion soft duration / Hybrid expiration).</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Int, RuntimeOverlay = true, Group = "TTL", DisplayName = "Data Cache TTL (seconds)")]
    public int? TtlSeconds { get; set; }

}
