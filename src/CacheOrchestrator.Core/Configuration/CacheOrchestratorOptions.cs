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
    /// Stable process identity for Local Admin, cluster bus anti-echo, and diagnostics.
    /// Bound from <c>Cache:InstanceId</c>. When empty, the host machine name is used.
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), emit client-visible diagnostic response headers
    /// such as <c>X-Cache</c>. Set to <see langword="false"/> in production if you prefer not to
    /// expose cache hit/miss and domain details to clients. Does not affect metrics, tracing, or logs.
    /// Bound from <c>Cache:EmitDiagnosticsHeaders</c>.
    /// </summary>
    public bool EmitDiagnosticsHeaders { get; set; } = true;

    /// <summary>
    /// Meter / Prometheus label options. Bound from <c>Cache:Metrics</c>.
    /// </summary>
    public MetricsOptions Metrics { get; set; } = new();

    /// <summary>
    /// Soft/hard timeouts and circuit breaker for distributed L2 for data-cache providers
    /// (any non-InMemory backend). Bound from <c>Cache:Distributed</c>.
    /// </summary>
    public DistributedResilienceOptions Distributed { get; set; } = new();

    /// <summary>Configuration for the Output Cache (HTTP response caching).</summary>
    public ProviderOptions OutputCache { get; set; } = new();

    /// <summary>
    /// Named data-cache instances. Each entry defines an independent L1+L2 cache with its own
    /// provider (built-in: <c>InMemory</c>; others via the FusionCache / HybridCache packages,
    /// e.g. <c>AddRedisFusionCacheBackend</c> or web meta <c>AddRedisBackend</c>).
    /// Not Output Cache <c>AddBackend</c> — that registers <see cref="OutputCache"/> stores only.
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
    /// Local Admin API settings (live stats, invalidate, runtime version/TTL).
    /// Bound from <c>Cache:Admin</c>. Disabled by default (zero cost).
    /// </summary>
    public AdminOptions Admin { get; set; } = new();

    /// <summary>
    /// Cluster command bus settings (optional multi-instance command distribution).
    /// Bound from <c>Cache:Cluster</c>. Disabled by default; requires <c>CacheOrchestrator.HttpBus</c> for HTTP transport.
    /// </summary>
    public ClusterOptions Cluster { get; set; } = new();

    /// <summary>The final namespace used for Output Cache keys.</summary>
    public string OutputNamespace => OutputCache.Namespace ?? (Namespace + "-oc");

    /// <summary>Effective distributed resilience settings for data-cache L2.</summary>
    public DistributedResilienceOptions GetEffectiveDistributedResilience() => Distributed;

    // ---------------------------------------------------------------------------
    // Nested types
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Options for <c>CacheOrchestrator</c> meter labels (OpenTelemetry / Prometheus).
    /// Bound from <c>Cache:Metrics</c>.
    /// </summary>
    public sealed class MetricsOptions
    {
        /// <summary>
        /// When <see langword="true"/> (default), OC/FC instruments include a stable
        /// <c>route</c> tag (<c>METHOD</c> + route template, same shape as Admin endpoint keys).
        /// Set <see langword="false"/> to emit only <c>domain</c> / <c>result</c> (lower cardinality).
        /// </summary>
        public bool IncludeEndpointLabel { get; set; } = true;
    }

    /// <summary>
    /// Local Admin API feature flags and auth. Bound from <c>Cache:Admin</c>.
    /// </summary>
    public sealed class AdminOptions
    {
        /// <summary>
        /// When <see langword="false"/> (default), no live counters, no admin routes, no runtime overlay activity.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Shared secret for header <c>X-Cache-Admin-Key</c>. When empty and <see cref="Enabled"/> is true,
        /// endpoints are open (intended for local development only).
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>Base path for Local Admin endpoints. Default: <c>/cache-admin/local</c>.</summary>
        public string RoutePrefix { get; set; } = "/cache-admin/local";

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
    /// Cluster-wide command distribution. Bound from <c>Cache:Cluster</c>.
    /// </summary>
    public sealed class ClusterOptions
    {
        /// <summary>HTTP (or other) command bus settings. Bound from <c>Cache:Cluster:Bus</c>.</summary>
        public ClusterBusOptions Bus { get; set; } = new();
    }

    /// <summary>
    /// Optional cluster command bus. Bound from <c>Cache:Cluster:Bus</c>.
    /// Transport implementations live in <c>CacheOrchestrator.HttpBus</c>.
    /// </summary>
    public sealed class ClusterBusOptions
    {
        /// <summary>
        /// When <see langword="false"/> (default), bus publish is a no-op even if the Bus package is registered.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>Per-peer HTTP timeout in milliseconds. Default: 2000.</summary>
        public int PeerTimeoutMs { get; set; } = 2000;

        /// <summary>Max parallel peer deliveries. Default: 32.</summary>
        public int MaxParallelism { get; set; } = 32;

        /// <summary>
        /// Membership strategy: <c>Null</c> (default), <c>Static</c>, or <c>ServiceDiscovery</c>.
        /// </summary>
        public string Membership { get; set; } = "Null";

        /// <summary>
        /// Shared secret for cluster receive endpoints (<c>X-Cache-Admin-Key</c>).
        /// When empty, falls back to <see cref="AdminOptions.ApiKey"/>.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Sliding window in seconds for ignoring duplicate <c>CommandId</c> values on receive.
        /// Default: 60. Set to 0 to disable dedupe.
        /// </summary>
        public int DedupeWindowSeconds { get; set; } = 60;

        /// <summary>Static peer list when <see cref="Membership"/> is <c>Static</c>.</summary>
        public StaticClusterMembershipOptions Static { get; set; } = new();

        /// <summary>Service discovery settings when <see cref="Membership"/> is <c>ServiceDiscovery</c>.</summary>
        public ServiceDiscoveryMembershipOptions ServiceDiscovery { get; set; } = new();
    }

    /// <summary>Static peer list for the cluster bus. Bound from <c>Cache:Cluster:Bus:Static</c>.</summary>
    public sealed class StaticClusterMembershipOptions
    {
        /// <summary>Peer instances (id + base URL).</summary>
        public List<StaticClusterPeerOptions> Instances { get; set; } = [];
    }

    /// <summary>One static peer entry.</summary>
    public sealed class StaticClusterPeerOptions
    {
        /// <summary>Peer id (should match that process's <c>Cache:InstanceId</c>).</summary>
        public string? Id { get; set; }

        /// <summary>Base URL (e.g. <c>http://10.0.0.1:8080</c>).</summary>
        public string? Url { get; set; }
    }

    /// <summary>
    /// Service discovery membership. Bound from <c>Cache:Cluster:Bus:ServiceDiscovery</c>.
    /// Uses <c>Microsoft.Extensions.ServiceDiscovery</c> (config, DNS, platform resolvers).
    /// </summary>
    public sealed class ServiceDiscoveryMembershipOptions
    {
        /// <summary>
        /// Logical service name to resolve (e.g. <c>app1</c> or <c>https+http://app1</c>).
        /// Typically aligns with the application / <c>Cache:Namespace</c> boundary.
        /// </summary>
        public string? ServiceName { get; set; }

        /// <summary>
        /// URI scheme used when resolved endpoints have no scheme (default <c>http</c>).
        /// </summary>
        public string DefaultScheme { get; set; } = "http";

        /// <summary>
        /// How long (seconds) to cache resolved peers in-process. Default: 15. Min 1 when caching.
        /// </summary>
        public int CacheSeconds { get; set; } = 15;
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
        /// When <see cref="Namespace"/> is unset: <c>{root.Namespace}-fc</c> for the
        /// <c>default</c> instance (no <c>-default</c> suffix), otherwise
        /// <c>{root.Namespace}-fc-{instanceName}</c>.
        /// </remarks>
        public string GetNamespace(string instanceName, CacheOrchestratorOptions root)
        {
            if (!string.IsNullOrWhiteSpace(Namespace))
                return Namespace;

            if (string.Equals(instanceName, "default", StringComparison.OrdinalIgnoreCase))
                return $"{root.Namespace}-fc";

            return $"{root.Namespace}-fc-{instanceName}";
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

        /// <summary>Output Cache policy for this domain.</summary>
        public DomainOutputCacheSettings? OutputCache { get; set; }

        /// <summary>Client Cache-Control policy for this domain.</summary>
        public DomainClientCacheSettings? ClientCache { get; set; }

        /// <summary>
        /// When Output Cache (and optionally data cache) auto-bypasses for auth traffic.
        /// When unset, defaults to <see cref="AuthBypassMode.AuthenticatedOrAuthorization"/>.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Enum, RuntimeOverlay = true, Group = "Cache", DisplayName = "Auth bypass mode")]
        public AuthBypassMode? AuthBypassMode { get; set; }

        /// <summary>
        /// When authentication is not bypassed, vary Output Cache by user identity (default true).
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Vary Output Cache by user")]
        public bool? VaryOutputCacheByUser { get; set; }

        /// <summary>
        /// When <see langword="true"/> (default), an <c>Authorization</c> header counts as an auth signal.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Treat Authorization as auth signal")]
        public bool? TreatAuthorizationAsAuthSignal { get; set; }

        /// <summary>
        /// When <see langword="true"/> (default), hash <c>Authorization</c> into auth-user vary when no identity claims.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Auth vary include Authorization hash")]
        public bool? AuthVaryIncludeAuthorizationHash { get; set; }

        /// <summary>Claim types to include in auth vary material.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Cache", DisplayName = "Vary by auth claims")]
        public string[]? VaryByAuthClaims { get; set; }

        /// <summary>
        /// When <see langword="true"/> (default), data cache skips when auth bypass would fire for OC.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Data", DisplayName = "Data cache respects auth bypass")]
        public bool? DataCacheRespectAuthBypass { get; set; }

        /// <summary>Vary by <c>Accept</c> header. Default in v3: true.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by Accept")]
        public bool? VaryByAccept { get; set; }

        /// <summary>Optional prefer-list for Accept normalization.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Accept normalization")]
        public string[]? AcceptNormalizationList { get; set; }

        /// <summary>Vary by <c>Accept-Language</c>.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by Accept-Language")]
        public bool? VaryByAcceptLanguage { get; set; }

        /// <summary>Optional prefer-list for Accept-Language normalization.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Accept-Language normalization")]
        public string[]? AcceptLanguageNormalizationList { get; set; }

        /// <summary>Extra request headers to vary on.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by headers")]
        public string[]? VaryByHeaders { get; set; }

        /// <summary>
        /// Query key allowlist. Null = all non-tracking (historical); empty = none; non-empty = allowlist.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by query keys")]
        public string[]? VaryByQueryKeys { get; set; }

        /// <summary>Extra query keys to ignore beyond built-in tracking prefixes.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Ignore query keys")]
        public string[]? IgnoreQueryKeys { get; set; }

        /// <summary>Cookie-name allowlist for vary (values always hashed).</summary>
        [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = true, Group = "Vary", DisplayName = "Vary by cookies")]
        public string[]? VaryByCookies { get; set; }

        /// <summary>
        /// Emit HTTP response <c>Vary</c> for non-secret headers we varied on.
        /// Default when unset: <see langword="true"/>.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Vary", DisplayName = "Emit response Vary")]
        public bool? EmitResponseVary { get; set; }
    }

    /// <summary>Provider selection for Output Cache (or similar single-provider surfaces).</summary>
    public sealed class ProviderOptions
    {
        /// <summary>Optional key namespace override for this provider.</summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// Storage provider name. Built-in: <c>InMemory</c>.
        /// Additional providers via backend packages / <c>AddBackend</c>.
        /// </summary>
        public string Provider { get; set; } = "InMemory";
    }
}
