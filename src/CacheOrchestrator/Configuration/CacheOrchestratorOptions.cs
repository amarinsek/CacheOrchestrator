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
    /// When <see langword="true"/> (default), emit client-visible diagnostic response headers
    /// such as <c>X-Cache</c>. Set to <see langword="false"/> in production if you prefer not to
    /// expose cache hit/miss and domain details to clients. Does not affect metrics, tracing, or logs.
    /// Bound from <c>Cache:EmitDiagnosticsHeaders</c>.
    /// </summary>
    public bool EmitDiagnosticsHeaders { get; set; } = true;

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

    /// <summary>The final namespace used for Output Cache keys.</summary>
    public string OutputNamespace => OutputCache.Namespace ?? (Namespace + "-oc");

    /// <summary>Effective distributed resilience settings for FusionCache L2.</summary>
    public DistributedResilienceOptions GetEffectiveDistributedResilience() => Distributed;

    // ---------------------------------------------------------------------------
    // Nested types
    // ---------------------------------------------------------------------------

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
        public string? FusionCacheInstance { get; set; }

        /// <summary>Enable Output Cache for this domain.</summary>
        public bool? OutputCacheEnabled { get; set; }

        /// <summary>Enable FusionCache for this domain.</summary>
        public bool? FusionCacheEnabled { get; set; }

        /// <summary>
        /// When <see langword="true"/> (default), skip Output Cache for authenticated users /
        /// <c>Authorization</c> header.
        /// </summary>
        public bool? BypassWhenAuthenticated { get; set; }

        /// <summary>
        /// When authentication is not bypassed, vary Output Cache by user identity (default true).
        /// </summary>
        public bool? VaryOutputCacheByUser { get; set; }

        /// <summary>
        /// Optional version token (e.g. "v1", "2026-08") used for bulk invalidation.
        /// If not set, a stable default ("1") is used so the cache never auto-invalidates on restart.
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// How the Output Cache policy sets the HTTP <c>ETag</c> header.
        /// Default: <see cref="ETagMode.Version"/>.
        /// </summary>
        public ETagMode? ETagMode { get; set; }

        /// <summary>HTTP status codes that may be stored in Output Cache.</summary>
        public int[]? CacheableStatusCodes { get; set; }

        /// <summary>Preferred Accept-Encoding values for normalization.</summary>
        public string[]? EncodingNormalizationList { get; set; }

        /// <summary>Client cache mode. Default: Public.</summary>
        public ClientCacheability? ClientCacheability { get; set; }

        /// <summary>Desired max-age far from update (seconds). Default: 3600.</summary>
        public int? ClientTtlSeconds { get; set; }

        /// <summary>Floor max-age near/at update (seconds). Default: 60.</summary>
        public int? ClientTtlMinSeconds { get; set; }

        /// <summary>Next planned content cutover (UTC). Null = always use ClientTtlSeconds.</summary>
        public DateTimeOffset? ScheduledUpdateUtc { get; set; }

        /// <summary>Append must-revalidate when max-age is at or below min. Default: false.</summary>
        public bool? ClientMustRevalidateNearUpdate { get; set; }

        /// <summary>Output Cache entry TTL (seconds).</summary>
        public int? OutputCacheTtlSeconds { get; set; }

        /// <summary>FusionCache soft (logical) duration (seconds).</summary>
        public int? FusionCacheSoftTtlSeconds { get; set; }

        /// <summary>FusionCache hard (absolute) duration cap (seconds).</summary>
        public int? FusionCacheHardTtlSeconds { get; set; }

        /// <summary>FusionCache fail-safe max duration (seconds).</summary>
        public int? FusionCacheFailSafeSeconds { get; set; }

        /// <summary>Eager refresh threshold ratio (0–1 exclusive). 0 = disabled.</summary>
        public double? FusionCacheEagerRefreshRatio { get; set; }

        /// <summary>Max jitter added to FusionCache duration (seconds).</summary>
        public int? FusionCacheJitterSeconds { get; set; }

        /// <summary>Factory soft timeout (seconds).</summary>
        public int? FusionCacheFactorySoftTimeoutSeconds { get; set; }

        /// <summary>Factory hard timeout (seconds).</summary>
        public int? FusionCacheFactoryHardTimeoutSeconds { get; set; }

        /// <summary>Optional max item size for memory cache (bytes). 0 = unlimited.</summary>
        public int? FusionCacheMaxItemBytes { get; set; }

        /// <summary>When true, skip FusionCache if the request has Cache-Control: no-store.</summary>
        public bool? FusionCacheRespectNoStore { get; set; }

        /// <summary>Allow background distributed cache operations.</summary>
        public bool? FusionCacheAllowBackgroundDistributed { get; set; }

        /// <summary>Allow background backplane operations.</summary>
        public bool? FusionCacheAllowBackgroundBackplane { get; set; }

        /// <summary>Include scheme/host in the FusionCache key.</summary>
        public bool? FusionCacheVaryOnPublicAddress { get; set; }

        /// <summary>Include Accept-Encoding in the FusionCache key.</summary>
        public bool? FusionCacheVaryOnEncoding { get; set; }
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
