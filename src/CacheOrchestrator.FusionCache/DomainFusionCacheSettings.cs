using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.FusionCache;

/// <summary>
/// FusionCache-specific knobs. Bound from <c>Cache:DomainDefaults:FusionCache</c> /
/// <c>Cache:Domains:{name}:FusionCache</c> (JSON section name stays <c>FusionCache</c>).
/// </summary>
public sealed class DomainFusionCacheSettings
{
    /// <summary>Hard (absolute) duration cap.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "TTL", DisplayName = "Fusion hard TTL")]
    public TimeSpan? HardTtl { get; set; }

    /// <summary>Fail-safe max duration.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "TTL", DisplayName = "Fusion fail-safe")]
    public TimeSpan? FailSafe { get; set; }

    /// <summary>Eager refresh threshold ratio (0–1 exclusive). 0 = disabled.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Double, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Eager refresh ratio")]
    public double? EagerRefreshRatio { get; set; }

    /// <summary>Max jitter added to Fusion duration.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Fusion jitter")]
    public TimeSpan? Jitter { get; set; }

    /// <summary>Factory soft timeout.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Factory soft timeout")]
    public TimeSpan? FactorySoftTimeout { get; set; }

    /// <summary>Factory hard timeout.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.TimeSpan, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Factory hard timeout")]
    public TimeSpan? FactoryHardTimeout { get; set; }

    /// <summary>Optional max item size for memory cache (bytes). 0 = unlimited.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Int, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Max item bytes")]
    public int? MaxItemBytes { get; set; }

    /// <summary>Allow background distributed cache operations.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Background distributed ops")]
    public bool? AllowBackgroundDistributed { get; set; }

    /// <summary>Allow background backplane operations.</summary>
    [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Background backplane ops")]
    public bool? AllowBackgroundBackplane { get; set; }
}
