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
    /// Soft/hard timeouts and circuit breaker for distributed FusionCache L2 (any non-InMemory provider).
    /// Bound from <c>Cache:Distributed</c>.
    /// </summary>
    public DistributedResilienceOptions Distributed { get; set; } = new();

    /// <summary>Configuration for the Output Cache (HTTP response caching).</summary>
    public ProviderOptions OutputCache { get; set; } = new();

    /// <summary>
    /// Named FusionCache instances. Each entry defines an independent L1+L2 cache with its own
    /// provider (built-in: <c>InMemory</c>; others via <see cref="DependencyInjection.ICacheOrchestratorBuilder.AddBackend"/>).
    /// At least one entry named <c>"default"</c> must be present.
    /// </summary>
    public Dictionary<string, FusionCacheInstanceOptions> FusionCacheInstances { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = new FusionCacheInstanceOptions()
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
    /// Bound from <c>Cache:Cluster</c>. Disabled by default; requires <c>CacheOrchestrator.Bus</c> for HTTP transport.
    /// </summary>
    public ClusterOptions Cluster { get; set; } = new();

    /// <summary>The final namespace used for Output Cache keys.</summary>
    public string OutputNamespace => OutputCache.Namespace ?? (Namespace + "-oc");

    /// <summary>Effective distributed resilience settings for FusionCache L2.</summary>
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
    /// Transport implementations live in <c>CacheOrchestrator.Bus</c>.
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
    /// Configuration for a single named FusionCache instance.
    /// </summary>
    public sealed class FusionCacheInstanceOptions
    {
        /// <summary>Optional key namespace override for this instance.</summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// Storage provider name. Built-in: <c>InMemory</c>.
        /// Additional providers are registered via the builder (e.g. Redis package <c>AddRedisBackend</c>).
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
        /// Name of the <see cref="FusionCacheInstances"/> entry this domain uses.
        /// <see langword="null"/> or absent uses the <c>"default"</c> instance.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.String, RuntimeOverlay = false, Group = "Fusion", DisplayName = "FusionCache instance")]
        public string? FusionCacheInstance { get; set; }

        /// <summary>Enable Output Cache for this domain.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Output Cache enabled")]
        public bool? OutputCacheEnabled { get; set; }

        /// <summary>Enable FusionCache for this domain.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Fusion", DisplayName = "FusionCache enabled")]
        public bool? FusionCacheEnabled { get; set; }

        /// <summary>
        /// When <see langword="true"/> (default), skip Output Cache for authenticated users /
        /// <c>Authorization</c> header.
        /// </summary>
        /// <remarks>
        /// Obsolete: prefer <see cref="AuthBypassMode"/>.
        /// <see langword="true"/> maps to <see cref="AuthBypassMode.AuthenticatedOrAuthorization"/>;
        /// <see langword="false"/> maps to <see cref="AuthBypassMode.Never"/>.
        /// Ignored when <see cref="AuthBypassMode"/> is set.
        /// </remarks>
        [Obsolete("Use AuthBypassMode. true → AuthenticatedOrAuthorization; false → Never. Ignored when AuthBypassMode is set.")]
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Bypass when authenticated (legacy)")]
        public bool? BypassWhenAuthenticated { get; set; }

        /// <summary>
        /// When Output Cache (and optionally FusionCache) auto-bypasses for auth traffic.
        /// When unset, derived from <see cref="BypassWhenAuthenticated"/>.
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
        /// When <see langword="true"/> (default), FusionCache skips cache read/write when auth bypass would fire for OC.
        /// Set <see langword="false"/> to keep Fusion caching while Output Cache auth-bypasses (pre-parity behaviour).
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Fusion respects auth bypass")]
        public bool? FusionRespectAuthBypass { get; set; }

        /// <summary>
        /// When <see langword="true"/> (default), force client Private when Identity is authenticated and cacheability is Public.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Client", DisplayName = "Force private when authenticated")]
        public bool? ClientForcePrivateWhenAuthenticated { get; set; }

        /// <summary>Vary by <c>Accept</c> header.</summary>
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
        /// Default when unset: <see langword="true"/>. Set <see langword="false"/> to omit it.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Vary", DisplayName = "Emit response Vary")]
        public bool? EmitResponseVary { get; set; }

        /// <summary>
        /// Optional version token (e.g. "v1", "2026-08") used for bulk invalidation.
        /// If not set, a stable default ("1") is used so the cache never auto-invalidates on restart.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.String, RuntimeOverlay = false, Group = "Cache", DisplayName = "Version")]
        public string? Version { get; set; }

        /// <summary>
        /// How the Output Cache policy sets the HTTP <c>ETag</c> header.
        /// Default: <see cref="ETagMode.Version"/>.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Enum, RuntimeOverlay = true, Group = "Cache", DisplayName = "ETag mode")]
        public ETagMode? ETagMode { get; set; }

        /// <summary>HTTP status codes that may be stored in Output Cache.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntArray, RuntimeOverlay = false, Group = "Cache", DisplayName = "Cacheable status codes")]
        public int[]? CacheableStatusCodes { get; set; }

        /// <summary>Preferred Accept-Encoding values for normalization.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.StringArray, RuntimeOverlay = false, Group = "Cache", DisplayName = "Encoding normalization")]
        public string[]? EncodingNormalizationList { get; set; }

        /// <summary>Client cache mode. Default: Public.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Enum, RuntimeOverlay = true, Group = "Client", DisplayName = "Client cacheability")]
        public ClientCacheability? ClientCacheability { get; set; }

        /// <summary>Desired max-age far from update (seconds). Default: 3600.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntSeconds, RuntimeOverlay = true, Group = "Client", DisplayName = "Client TTL (s)")]
        public int? ClientTtlSeconds { get; set; }

        /// <summary>Floor max-age near/at update (seconds). Default: 60.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntSeconds, RuntimeOverlay = true, Group = "Client", DisplayName = "Client TTL min (s)")]
        public int? ClientTtlMinSeconds { get; set; }

        /// <summary>Next planned content cutover (UTC). Null = always use ClientTtlSeconds.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.DateTimeOffset, RuntimeOverlay = true, Group = "Client", DisplayName = "Scheduled update (UTC)")]
        public DateTimeOffset? ScheduledUpdateUtc { get; set; }

        /// <summary>Append must-revalidate when max-age is at or below min. Default: false.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Client", DisplayName = "Must-revalidate near update")]
        public bool? ClientMustRevalidateNearUpdate { get; set; }

        /// <summary>Output Cache entry TTL (seconds).</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntSeconds, RuntimeOverlay = true, Group = "TTL", DisplayName = "Output Cache TTL (s)")]
        public int? OutputCacheTtlSeconds { get; set; }

        /// <summary>FusionCache soft (logical) duration (seconds).</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntSeconds, RuntimeOverlay = true, Group = "TTL", DisplayName = "Fusion soft TTL (s)")]
        public int? FusionCacheSoftTtlSeconds { get; set; }

        /// <summary>FusionCache hard (absolute) duration cap (seconds).</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntSeconds, RuntimeOverlay = true, Group = "TTL", DisplayName = "Fusion hard TTL (s)")]
        public int? FusionCacheHardTtlSeconds { get; set; }

        /// <summary>FusionCache fail-safe max duration (seconds).</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntSeconds, RuntimeOverlay = true, Group = "TTL", DisplayName = "Fusion fail-safe (s)")]
        public int? FusionCacheFailSafeSeconds { get; set; }

        /// <summary>Eager refresh threshold ratio (0–1 exclusive). 0 = disabled.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Double, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Eager refresh ratio")]
        public double? FusionCacheEagerRefreshRatio { get; set; }

        /// <summary>Max jitter added to FusionCache duration (seconds).</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntSeconds, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Fusion jitter (s)")]
        public int? FusionCacheJitterSeconds { get; set; }

        /// <summary>Factory soft timeout (seconds).</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntSeconds, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Factory soft timeout (s)")]
        public int? FusionCacheFactorySoftTimeoutSeconds { get; set; }

        /// <summary>Factory hard timeout (seconds).</summary>
        [DomainSetting(Kind = DomainSettingValueKind.IntSeconds, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Factory hard timeout (s)")]
        public int? FusionCacheFactoryHardTimeoutSeconds { get; set; }

        /// <summary>Optional max item size for memory cache (bytes). 0 = unlimited.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Int, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Max item bytes")]
        public int? FusionCacheMaxItemBytes { get; set; }

        /// <summary>When true, skip FusionCache if the request has Cache-Control: no-store.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Respect no-store")]
        public bool? FusionCacheRespectNoStore { get; set; }

        /// <summary>Allow background distributed cache operations.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Background distributed ops")]
        public bool? FusionCacheAllowBackgroundDistributed { get; set; }

        /// <summary>Allow background backplane operations.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Background backplane ops")]
        public bool? FusionCacheAllowBackgroundBackplane { get; set; }

        /// <summary>Include scheme/host in the FusionCache key.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Vary on public address")]
        public bool? FusionCacheVaryOnPublicAddress { get; set; }

        /// <summary>Include Accept-Encoding in the FusionCache key.</summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Fusion", DisplayName = "Vary on encoding")]
        public bool? FusionCacheVaryOnEncoding { get; set; }

        /// <summary>
        /// When true (default), Output Cache varies by host (includes port).
        /// Set false so multi-host/port entry points share the same OC entry.
        /// </summary>
        [DomainSetting(Kind = DomainSettingValueKind.Bool, RuntimeOverlay = true, Group = "Cache", DisplayName = "Vary Output Cache by host")]
        public bool? OutputCacheVaryByHost { get; set; }
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
