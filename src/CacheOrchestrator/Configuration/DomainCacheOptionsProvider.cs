using CacheOrchestrator.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Text;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Resolves and caches effective <see cref="DomainCacheOptions"/> per domain.
/// </summary>
/// <remarks>
/// Not part of the stable public surface — resolve via <see cref="IDomainCacheOptionsProvider"/>.
/// Applies process-local <see cref="IDomainRuntimeOverrideStore"/> overlays (Admin Version/TTL) when present.
/// </remarks>
internal sealed class DomainCacheOptionsProvider : IDomainCacheOptionsProvider, IDisposable
{
    private readonly ILogger<DomainCacheOptionsProvider> _logger;
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _optionsMonitor;
    private readonly IDomainRuntimeOverrideStore _runtimeOverrides;
    private readonly ConcurrentDictionary<string, CachedDomainOptions> _globalCache = new(StringComparer.Ordinal);
    private readonly IDisposable? _changeRegistration;

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainCacheOptionsProvider"/> class.
    /// </summary>
    public DomainCacheOptionsProvider(
        IOptionsMonitor<CacheOrchestratorOptions> optionsMonitor,
        ILogger<DomainCacheOptionsProvider> logger,
        IDomainRuntimeOverrideStore? runtimeOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _runtimeOverrides = runtimeOverrides ?? NullDomainRuntimeOverrideStore.Instance;

        _changeRegistration = _optionsMonitor.OnChange(_ =>
        {
            _logger.LogInformation(
                "Configuration changed: clearing global config cache (purged {Count} items).",
                _globalCache.Count);
            _globalCache.Clear();
        });
    }

    private sealed class CachedDomainOptions
    {
        public required DomainCacheOptions Options { get; init; }
        public int OverrideStamp { get; init; }
    }

    /// <inheritdoc />
    public void Dispose() => _changeRegistration?.Dispose();

    /// <inheritdoc />
    public DomainCacheOptions EnsureDomainOptions(HttpContext http, string domain)
    {
        ArgumentNullException.ThrowIfNull(http);

        // L1: per-request HttpContext.Items
        if (http.Items.TryGetValue(CacheOrchestratorKeys.DomainOptionsKey, out object? obj) && obj is DomainCacheOptions cached)
            return cached;
        // L2: process-wide ConcurrentDictionary
        DomainCacheOptions resolved = GetOrCreateDomainOptions(domain);
        http.Items[CacheOrchestratorKeys.DomainOptionsKey] = resolved;
        return resolved;
    }

    /// <inheritdoc />
    public DomainCacheOptions? GetDomainOptions(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items[CacheOrchestratorKeys.DomainOptionsKey] as DomainCacheOptions;
    }

    /// <inheritdoc />
    public DomainCacheOptions GetOrCreateDomainOptions(string domain)
    {
        domain = DomainName.Normalize(domain);
        int stamp = _runtimeOverrides.GetStamp(domain);

        if (_globalCache.TryGetValue(domain, out CachedDomainOptions? cached)
            && cached.OverrideStamp == stamp)
        {
            return cached.Options;
        }

        DomainCacheOptions options = CreateDomainOptions(domain);
        CachedDomainOptions entry = new() { Options = options, OverrideStamp = stamp };
        _globalCache[domain] = entry;
        return options;
    }

    private DomainCacheOptions CreateDomainOptions(string domain)
    {
        CacheOrchestratorOptions options = _optionsMonitor.CurrentValue;
        CacheOrchestratorOptions.DomainCacheSettings defaults = options.DomainDefaults;
        DomainRuntimeOverride? overlay = _runtimeOverrides.Get(domain);

        if (!options.Domains.TryGetValue(domain, out CacheOrchestratorOptions.DomainCacheSettings? dom))
        {
            _logger.LogWarning(
                "Domain '{Domain}' is not configured. Falling back to DomainDefaults. Using domain name '{ResolvedDomain}'.",
                domain,
                domain == DomainName.Default ? DomainName.Default : domain);
            dom = new CacheOrchestratorOptions.DomainCacheSettings();
        }

        static T Pick<T>(T? specific, T? global, T fallback) where T : struct =>
            specific ?? global ?? fallback;

        string version;
        bool usedDefaultVersion = false;

        if (overlay?.Version is { Length: > 0 } overlayVersion)
        {
            version = overlayVersion;
        }
        else if (!string.IsNullOrWhiteSpace(dom.Version))
        {
            version = dom.Version;
        }
        else if (!string.IsNullOrWhiteSpace(defaults.Version))
        {
            version = defaults.Version;
        }
        else
        {
            // Stable default so keys do not change across restarts without an explicit Version.
            version = "1";
            usedDefaultVersion = true;
        }

        if (usedDefaultVersion)
        {
            _logger.LogWarning(
                "Domain '{Domain}' has no Version configured (neither in domain nor in DomainDefaults). " +
                "Using stable default ('1'). Cache will not auto-invalidate on restart. " +
                "Set Version if you want controlled invalidation.",
                domain);
        }

        // Resolve FusionCache instance name: domain → defaults → "default"
        string instanceName = !string.IsNullOrWhiteSpace(dom.FusionCacheInstance)
            ? dom.FusionCacheInstance
            : !string.IsNullOrWhiteSpace(defaults.FusionCacheInstance)
                ? defaults.FusionCacheInstance
                : "default";

        ulong versionHash = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(version));
        string versionHex = versionHash.ToString("x16");
        ETagMode etagMode = overlay?.ETagMode
            ?? dom.ETagMode
            ?? defaults.ETagMode
            ?? ETagMode.Version;
        StringValues etag = CacheETagFactory.FromVersion(version);

        int outputTtlSeconds = overlay?.OutputCacheTtlSeconds
            ?? Pick(dom.OutputCacheTtlSeconds, defaults.OutputCacheTtlSeconds, 3700);
        int fusionSoftSeconds = overlay?.FusionCacheSoftTtlSeconds
            ?? Pick(dom.FusionCacheSoftTtlSeconds, defaults.FusionCacheSoftTtlSeconds, 3800);
        int fusionHardSeconds = overlay?.FusionCacheHardTtlSeconds
            ?? Pick(dom.FusionCacheHardTtlSeconds, defaults.FusionCacheHardTtlSeconds, 43200);
        int fusionFailSafeSeconds = overlay?.FusionCacheFailSafeSeconds
            ?? Pick(dom.FusionCacheFailSafeSeconds, defaults.FusionCacheFailSafeSeconds, 86400);
        int clientTtlSeconds = overlay?.ClientTtlSeconds
            ?? Pick(dom.ClientTtlSeconds, defaults.ClientTtlSeconds, 3600);
        int clientTtlMinSeconds = overlay?.ClientTtlMinSeconds
            ?? Pick(dom.ClientTtlMinSeconds, defaults.ClientTtlMinSeconds, 60);

        AuthBypassMode authBypassMode = ResolveAuthBypassMode(overlay, dom, defaults);

        return new DomainCacheOptions
        {
            Domain = domain,
            FusionCacheInstanceName = instanceName,
            OutputCacheEnabled = overlay?.OutputCacheEnabled
                ?? Pick(dom.OutputCacheEnabled, defaults.OutputCacheEnabled, true),
            FusionCacheEnabled = overlay?.FusionCacheEnabled
                ?? Pick(dom.FusionCacheEnabled, defaults.FusionCacheEnabled, true),
            AuthBypassMode = authBypassMode,
            BypassWhenAuthenticated = authBypassMode != AuthBypassMode.Never,
            VaryOutputCacheByUser = overlay?.VaryOutputCacheByUser
                ?? Pick(dom.VaryOutputCacheByUser, defaults.VaryOutputCacheByUser, true),
            TreatAuthorizationAsAuthSignal = overlay?.TreatAuthorizationAsAuthSignal
                ?? Pick(dom.TreatAuthorizationAsAuthSignal, defaults.TreatAuthorizationAsAuthSignal, true),
            AuthVaryIncludeAuthorizationHash = overlay?.AuthVaryIncludeAuthorizationHash
                ?? Pick(dom.AuthVaryIncludeAuthorizationHash, defaults.AuthVaryIncludeAuthorizationHash, true),
            FusionRespectAuthBypass = overlay?.FusionRespectAuthBypass
                ?? Pick(dom.FusionRespectAuthBypass, defaults.FusionRespectAuthBypass, true),
            ClientForcePrivateWhenAuthenticated = overlay?.ClientForcePrivateWhenAuthenticated
                ?? Pick(dom.ClientForcePrivateWhenAuthenticated, defaults.ClientForcePrivateWhenAuthenticated, true),
            VaryByAccept = overlay?.VaryByAccept
                ?? Pick(dom.VaryByAccept, defaults.VaryByAccept, false),
            AcceptNormalizationList = overlay?.AcceptNormalizationList
                ?? dom.AcceptNormalizationList
                ?? defaults.AcceptNormalizationList,
            VaryByAcceptLanguage = overlay?.VaryByAcceptLanguage
                ?? Pick(dom.VaryByAcceptLanguage, defaults.VaryByAcceptLanguage, false),
            AcceptLanguageNormalizationList = overlay?.AcceptLanguageNormalizationList
                ?? dom.AcceptLanguageNormalizationList
                ?? defaults.AcceptLanguageNormalizationList,
            VaryByHeaders = overlay?.VaryByHeaders
                ?? dom.VaryByHeaders
                ?? defaults.VaryByHeaders,
            VaryByQueryKeys = overlay?.VaryByQueryKeys
                ?? dom.VaryByQueryKeys
                ?? defaults.VaryByQueryKeys,
            IgnoreQueryKeys = overlay?.IgnoreQueryKeys
                ?? dom.IgnoreQueryKeys
                ?? defaults.IgnoreQueryKeys,
            VaryByCookies = overlay?.VaryByCookies
                ?? dom.VaryByCookies
                ?? defaults.VaryByCookies,
            VaryByAuthClaims = overlay?.VaryByAuthClaims
                ?? dom.VaryByAuthClaims
                ?? defaults.VaryByAuthClaims,
            EmitResponseVary = overlay?.EmitResponseVary
                ?? Pick(dom.EmitResponseVary, defaults.EmitResponseVary, true),
            Version = version,
            VersionHex = versionHex,
            ETagMode = etagMode,
            ETag = etag,
            CacheableStatusCodes = dom.CacheableStatusCodes ?? defaults.CacheableStatusCodes ?? [200],
            EncodingNormalizationList = dom.EncodingNormalizationList ?? defaults.EncodingNormalizationList,

            ClientCacheability = overlay?.ClientCacheability
                ?? dom.ClientCacheability
                ?? defaults.ClientCacheability
                ?? ClientCacheability.Public,
            ClientTtlSeconds = clientTtlSeconds,
            ClientTtlMinSeconds = clientTtlMinSeconds,
            ScheduledUpdateUtc = overlay?.ScheduledUpdateUtc
                ?? dom.ScheduledUpdateUtc
                ?? defaults.ScheduledUpdateUtc,
            ClientMustRevalidateNearUpdate = overlay?.ClientMustRevalidateNearUpdate
                ?? Pick(dom.ClientMustRevalidateNearUpdate, defaults.ClientMustRevalidateNearUpdate, false),

            OutputTtl = TimeSpan.FromSeconds(Math.Max(0, outputTtlSeconds)),
            FusionCacheSoftTtl = TimeSpan.FromSeconds(fusionSoftSeconds),
            FusionCacheHardTtl = TimeSpan.FromSeconds(fusionHardSeconds),
            FusionCacheFailSafe = TimeSpan.FromSeconds(fusionFailSafeSeconds),

            OutputCacheNamespace = options.OutputNamespace,
            FusionCacheNamespace = options.FusionCacheInstances.TryGetValue(instanceName, out CacheOrchestratorOptions.FusionCacheInstanceOptions? inst)
                ? inst.GetNamespace(instanceName, options)
                : new CacheOrchestratorOptions.FusionCacheInstanceOptions().GetNamespace(instanceName, options),

            FusionCacheEagerRefreshRatio = overlay?.FusionCacheEagerRefreshRatio
                ?? Pick(dom.FusionCacheEagerRefreshRatio, defaults.FusionCacheEagerRefreshRatio, 0.9),
            FusionCacheJitterSeconds = overlay?.FusionCacheJitterSeconds
                ?? Pick(dom.FusionCacheJitterSeconds, defaults.FusionCacheJitterSeconds, 60),
            FusionCacheFactorySoftTimeoutSeconds = overlay?.FusionCacheFactorySoftTimeoutSeconds
                ?? Pick(dom.FusionCacheFactorySoftTimeoutSeconds, defaults.FusionCacheFactorySoftTimeoutSeconds, 1),
            FusionCacheFactoryHardTimeoutSeconds = overlay?.FusionCacheFactoryHardTimeoutSeconds
                ?? Pick(dom.FusionCacheFactoryHardTimeoutSeconds, defaults.FusionCacheFactoryHardTimeoutSeconds, 5),
            FusionCacheMaxItemBytes = overlay?.FusionCacheMaxItemBytes
                ?? Pick(dom.FusionCacheMaxItemBytes, defaults.FusionCacheMaxItemBytes, 0),
            FusionCacheRespectNoStore = overlay?.FusionCacheRespectNoStore
                ?? Pick(dom.FusionCacheRespectNoStore, defaults.FusionCacheRespectNoStore, true),
            FusionCacheAllowBackgroundDistributed = overlay?.FusionCacheAllowBackgroundDistributed
                ?? Pick(dom.FusionCacheAllowBackgroundDistributed, defaults.FusionCacheAllowBackgroundDistributed, true),
            FusionCacheAllowBackgroundBackplane = overlay?.FusionCacheAllowBackgroundBackplane
                ?? Pick(dom.FusionCacheAllowBackgroundBackplane, defaults.FusionCacheAllowBackgroundBackplane, true),
            FusionCacheVaryOnPublicAddress = overlay?.FusionCacheVaryOnPublicAddress
                ?? Pick(dom.FusionCacheVaryOnPublicAddress, defaults.FusionCacheVaryOnPublicAddress, true),
            FusionCacheVaryOnEncoding = overlay?.FusionCacheVaryOnEncoding
                ?? Pick(dom.FusionCacheVaryOnEncoding, defaults.FusionCacheVaryOnEncoding, true),
            OutputCacheVaryByHost = overlay?.OutputCacheVaryByHost
                ?? Pick(dom.OutputCacheVaryByHost, defaults.OutputCacheVaryByHost, true),
        };
    }

#pragma warning disable CS0618 // BypassWhenAuthenticated is obsolete — kept for config compat
    private static AuthBypassMode ResolveAuthBypassMode(
        DomainRuntimeOverride? overlay,
        CacheOrchestratorOptions.DomainCacheSettings dom,
        CacheOrchestratorOptions.DomainCacheSettings defaults)
    {
        if (overlay?.AuthBypassMode is AuthBypassMode overlayMode)
            return overlayMode;
        if (dom.AuthBypassMode is AuthBypassMode domMode)
            return domMode;
        if (defaults.AuthBypassMode is AuthBypassMode defaultsMode)
            return defaultsMode;

        // Legacy bool: overlay → domain → defaults → true
        bool legacy = overlay?.BypassWhenAuthenticated
            ?? dom.BypassWhenAuthenticated
            ?? defaults.BypassWhenAuthenticated
            ?? true;
        return legacy
            ? AuthBypassMode.AuthenticatedOrAuthorization
            : AuthBypassMode.Never;
    }
#pragma warning restore CS0618
}
