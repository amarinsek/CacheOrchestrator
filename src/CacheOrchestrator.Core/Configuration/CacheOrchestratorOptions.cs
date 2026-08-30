namespace CacheOrchestrator.Configuration;

/// <summary>
/// Strongly-typed caching configuration bound from appsettings.json (core package).
/// Provider-specific connection settings (e.g. Redis) live in the corresponding backend package.
/// </summary>
public sealed class CacheOrchestratorOptions
{
    /// <summary>Global application prefix used to isolate keys in shared storage.</summary>
    public string? Namespace { get; set; } = "app-cache";

    /// <summary>
    /// Stable process identity for Admin API, cluster bus anti-echo, and diagnostics.
    /// Bound from <c>Cache:InstanceId</c>. When empty, the host machine name is used.
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Soft/hard timeouts and circuit breaker for distributed L2 for data-cache providers
    /// (any non-InMemory backend). Bound from <c>Cache:Distributed</c>.
    /// </summary>
    public DistributedResilienceOptions Distributed { get; set; } = new();

    /// <summary>
    /// Named data-cache instances. Each entry defines an independent L1+L2 cache with its own
    /// provider (built-in: <c>InMemory</c>; others via the FusionCache / HybridCache packages,
    /// e.g. <c>AddRedisFusionCacheBackend</c> or web meta <c>AddRedisBackend</c>).
    /// Not Output Cache <c>AddBackend</c> — that registers host-owned Output Cache stores only.
    /// At least one entry named <c>"default"</c> must be present.
    /// Bound from <c>Cache:DataCacheInstances</c>.
    /// </summary>
    public Dictionary<string, DataCacheInstanceOptions> DataCacheInstances { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = new DataCacheInstanceOptions()
        };

    /// <summary>Global default settings applied to all domains unless specifically overridden.</summary>
    public DomainCacheSettings DomainDefaults { get; set; } = new();

    /// <summary>Per-domain overrides (keys are domain names).</summary>
    public Dictionary<string, DomainCacheSettings> Domains { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Transport-independent management instrumentation settings.
    /// Bound from <c>Cache:Admin</c>. Disabled by default.
    /// </summary>
    public AdminOptions Admin { get; set; } = new();

    /// <summary>Effective distributed resilience settings for data-cache L2.</summary>
    public DistributedResilienceOptions GetEffectiveDistributedResilience() => Distributed;

    // ---------------------------------------------------------------------------
    // Nested types
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Management instrumentation feature flags. Bound from <c>Cache:Admin</c>.
    /// </summary>
    public sealed class AdminOptions
    {
        /// <summary>
        /// When <see langword="false"/> (default), management live counters are disabled.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>When true (default), maintain per-endpoint counters in addition to per-domain.</summary>
        public bool TrackEndpoints { get; set; } = true;

        /// <summary>When true, track factory duration sum/count (more expensive). Default false.</summary>
        public bool TrackLatency { get; set; }

        /// <summary>
        /// When true, track factory result size sum/count for cheap-to-measure types
        /// (string, byte buffers, seekable streams). Default false.
        /// </summary>
        public bool TrackResultSize { get; set; }
    }

    /// <summary>
    /// Configuration for a single named data-cache instance.
    /// </summary>
    public sealed class DataCacheInstanceOptions
    {
        /// <summary>Optional key namespace override for this instance.</summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// Storage provider name. Built-in: <c>InMemory</c>.
        /// Additional providers come from the data-cache engine package (FusionCache / HybridCache),
        /// e.g. <c>AddFusionCacheBackend</c> / <c>AddRedisFusionCacheBackend</c>
        /// (web hosts may use meta <c>AddRedisBackend</c>, which also registers Fusion Redis).
        /// </summary>
        public string Provider { get; set; } = "InMemory";

        /// <summary>
        /// Returns the key namespace for this instance.
        /// </summary>
        /// <remarks>
        /// When <see cref="Namespace"/> is unset: <c>{root.Namespace}-dc</c> for the
        /// <c>default</c> instance (no <c>-default</c> suffix), otherwise
        /// <c>{root.Namespace}-dc-{instanceName}</c>.
        /// </remarks>
        public string GetNamespace(string instanceName, CacheOrchestratorOptions root)
        {
            if (!string.IsNullOrWhiteSpace(Namespace))
                return Namespace;

            if (string.Equals(instanceName, "default", StringComparison.OrdinalIgnoreCase))
                return $"{root.Namespace}-dc";

            return $"{root.Namespace}-dc-{instanceName}";
        }
    }

    /// <summary>Per-domain or default settings bound from configuration (nullable = inherit).</summary>
    public sealed class DomainCacheSettings
    {
        /// <summary>
        /// Optional version token (e.g. "v1", "2026-08") used for bulk invalidation.
        /// If not set, a stable default ("1") is used so the cache never auto-invalidates on restart.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.String, RuntimeOverlay = false, Group = "Cache", DisplayName = "Version")]
        public string? Version { get; set; }

        /// <summary>Portable Data Cache policy (TTL / enabled / instance).</summary>
        public DomainDataCacheSettings? DataCache { get; set; }

    }

}
