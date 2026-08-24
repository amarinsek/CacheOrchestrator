namespace CacheOrchestrator.Configuration;

/// <summary>
/// Resolved, effective cache settings for a single domain (immutable snapshot).
/// </summary>
public sealed class DomainCacheOptions
{
    /// <summary>Normalized domain name.</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>
    /// Name of the FusionCache instance that handles this domain.
    /// Matches a key in <see cref="CacheOrchestratorOptions.FusionCacheInstances"/>.
    /// </summary>
    public string FusionCacheInstanceName { get; init; } = "default";

    /// <summary>Whether Output Cache is enabled for this domain. Default: <see langword="true"/>.</summary>
    public bool OutputCacheEnabled { get; init; } = true;

    /// <summary>Whether data cache (Fusion / Hybrid) is enabled for this domain. Default: <see langword="true"/>.</summary>
    public bool DataCacheEnabled { get; init; } = true;

    /// <summary>
    /// When Output Cache (and optionally FusionCache) auto-bypasses for auth traffic.
    /// Default: <see cref="AuthBypassMode.AuthenticatedOrAuthorization"/> (historical behaviour).
    /// </summary>
    public AuthBypassMode AuthBypassMode { get; init; } = AuthBypassMode.AuthenticatedOrAuthorization;

    /// <summary>
    /// When caching is allowed for authenticated requests, include a per-user key in vary rules
    /// so users do not share each other's cached responses. Default: <see langword="true"/>.
    /// Set to <see langword="false"/> for shared public content that only happens to carry an API key.
    /// </summary>
    public bool VaryOutputCacheByUser { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/> (default), an <c>Authorization</c> header counts as an auth signal
    /// for <see cref="AuthBypassMode.AuthenticatedOrAuthorization"/> and user vary.
    /// </summary>
    public bool TreatAuthorizationAsAuthSignal { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/> (default) and no identity claims/name are available, hash the
    /// <c>Authorization</c> header into the auth-user vary segment.
    /// </summary>
    public bool AuthVaryIncludeAuthorizationHash { get; init; } = true;

    /// <summary>
    /// Optional claim types included in the auth-user vary material (e.g. <c>tenant_id</c>, <c>sub</c>).
    /// </summary>
    public string[]? VaryByAuthClaims { get; init; }

    /// <summary>
    /// When <see langword="true"/> (default), FusionCache <c>GetOrSet*</c> runs the factory uncached when
    /// <see cref="DomainAuthEvaluator.ShouldBypassForAuth"/> would fire for Output Cache.
    /// Set <see langword="false"/> only if you intentionally want Fusion to cache while OC auth-bypasses.
    /// </summary>
    public bool FusionRespectAuthBypass { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/> (default), clamp client <c>Cache-Control</c> from Public to Private
    /// for authenticated Identity users.
    /// </summary>
    public bool ClientForcePrivateWhenAuthenticated { get; init; } = true;

    /// <summary>Vary Output Cache / Fusion by the <c>Accept</c> header. Default: false (provider default is true in v3).</summary>
    public bool VaryByAccept { get; init; }

    /// <summary>Optional prefer-list for <c>Accept</c> normalization (same pattern as encoding).</summary>
    public string[]? AcceptNormalizationList { get; init; }

    /// <summary>Vary by <c>Accept-Language</c>. Default: false.</summary>
    public bool VaryByAcceptLanguage { get; init; }

    /// <summary>Optional prefer-list for <c>Accept-Language</c> normalization.</summary>
    public string[]? AcceptLanguageNormalizationList { get; init; }

    /// <summary>
    /// Extra request headers to vary on (case-insensitive). Sensitive names are hashed.
    /// Default: <see langword="null"/> (none).
    /// </summary>
    public string[]? VaryByHeaders { get; init; }

    /// <summary>
    /// Query-string allowlist for cache identity.
    /// <see langword="null"/> = all non-tracking keys (historical);
    /// empty = no query vary;
    /// non-empty = only these keys (tracking still stripped).
    /// </summary>
    public string[]? VaryByQueryKeys { get; init; }

    /// <summary>Extra query keys to ignore on top of built-in tracking prefixes.</summary>
    public string[]? IgnoreQueryKeys { get; init; }

    /// <summary>
    /// Cookie-name allowlist for vary (values always hashed). Default: <see langword="null"/> (never).
    /// </summary>
    public string[]? VaryByCookies { get; init; }

    /// <summary>
    /// When <see langword="true"/> (default), append HTTP response <c>Vary</c> for non-secret headers we varied on.
    /// Set <see langword="false"/> to omit the response <c>Vary</c> header (pre-2.x-like silence).
    /// </summary>
    public bool EmitResponseVary { get; init; } = true;

    /// <summary>Version token (e.g. "v1", "2026-08") used for bulk invalidation and ETag generation.</summary>
    public string Version { get; init; } = "1";

    /// <summary>Hex representation of the XxHash3 of the Version string. Used for cache keys.</summary>
    public string VersionHex { get; init; } = string.Empty;

    /// <summary>
    /// How the Output Cache policy sets the HTTP <c>ETag</c> header.
    /// Default: <see cref="ETagMode.Version"/> (domain generation stamp).
    /// </summary>
    public ETagMode ETagMode { get; init; } = ETagMode.Version;

    /// <summary>
    /// Precomputed weak ETag for <see cref="ETagMode.Version"/> (XxHash3 of Version).
    /// Ignored when <see cref="ETagMode"/> is <see cref="ETagMode.None"/> or <see cref="ETagMode.Resource"/>.
    /// </summary>
    public Microsoft.Extensions.Primitives.StringValues ETag { get; init; }

    /// <summary>HTTP status codes that may be stored in Output Cache.</summary>
    public int[] CacheableStatusCodes { get; init; } = [200];

    /// <summary>Preferred Accept-Encoding values for normalization (or null to skip).</summary>
    public string[]? EncodingNormalizationList { get; init; } = ["br", "gzip"];

    /// <summary>Client cache mode. Default: Public.</summary>
    public ClientCacheability ClientCacheability { get; init; }

    /// <summary>Desired max-age far from update (seconds). Default: 3600.</summary>
    public int ClientTtlSeconds { get; init; }

    /// <summary>Floor max-age near/at update (seconds). Default: 60.</summary>
    public int ClientTtlMinSeconds { get; init; }

    /// <summary>Next planned content cutover (UTC). Null = always use ClientTtlSeconds.</summary>
    public DateTimeOffset? ScheduledUpdateUtc { get; init; }

    /// <summary>Append must-revalidate when max-age is at or below min (optional). Default: false.</summary>
    public bool ClientMustRevalidateNearUpdate { get; init; }

    /// <summary>Output Cache entry duration.</summary>
    public TimeSpan OutputTtl { get; init; }

    /// <summary>Logical data-cache TTL (Fusion soft duration / Hybrid expiration).</summary>
    public TimeSpan DataCacheTtl { get; init; }

    /// <summary>FusionCache hard (absolute) duration cap for soft TTL.</summary>
    public TimeSpan FusionCacheHardTtl { get; init; }

    /// <summary>FusionCache fail-safe max duration when serving stale data.</summary>
    public TimeSpan FusionCacheFailSafe { get; init; }

    /// <summary>Key prefix / namespace for Output Cache.</summary>
    public string OutputCacheNamespace { get; init; } = string.Empty;

    /// <summary>Key prefix / namespace for FusionCache.</summary>
    public string FusionCacheNamespace { get; init; } = string.Empty;

    /// <summary>Eager refresh threshold ratio (0–1 exclusive), or 0 to disable.</summary>
    public double FusionCacheEagerRefreshRatio { get; init; }

    /// <summary>Max jitter added to FusionCache duration (seconds).</summary>
    public int FusionCacheJitterSeconds { get; init; }

    /// <summary>Factory soft timeout (seconds).</summary>
    public int FusionCacheFactorySoftTimeoutSeconds { get; init; }

    /// <summary>Factory hard timeout (seconds).</summary>
    public int FusionCacheFactoryHardTimeoutSeconds { get; init; }

    /// <summary>Optional max item size for memory cache (bytes); 0 = unlimited.</summary>
    public int FusionCacheMaxItemBytes { get; init; }

    /// <summary>When true (default), skip FusionCache if the request has Cache-Control: no-store.</summary>
    public bool FusionCacheRespectNoStore { get; init; } = true;

    /// <summary>Allow background distributed cache operations. Default: <see langword="true"/>.</summary>
    public bool FusionCacheAllowBackgroundDistributed { get; init; } = true;

    /// <summary>Allow background backplane operations. Default: <see langword="true"/>.</summary>
    public bool FusionCacheAllowBackgroundBackplane { get; init; } = true;

    /// <summary>Include Accept-Encoding in the FusionCache key. Default: <see langword="true"/>.</summary>
    public bool FusionCacheVaryOnEncoding { get; init; } = true;

    /// <summary>Include scheme/host in the FusionCache key. Default: <see langword="true"/>.</summary>
    public bool FusionCacheVaryOnPublicAddress { get; init; } = true;

    /// <summary>
    /// When true (default), Output Cache varies by request host (includes port).
    /// Set false when multiple public hosts/ports should share the same OC entry (e.g. multi-instance labs).
    /// </summary>
    public bool OutputCacheVaryByHost { get; init; } = true;
}
