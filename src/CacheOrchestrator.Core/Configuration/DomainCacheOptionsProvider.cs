using CacheOrchestrator.Admin;
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
    private static readonly string[] DefaultAcceptNormalization = ["application/json", "application/xml"];

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

        string? domInstance = dom.DataCache?.Instance;
        string? defaultsInstance = defaults.DataCache?.Instance;
        string instanceName = !string.IsNullOrWhiteSpace(domInstance)
            ? domInstance
            : !string.IsNullOrWhiteSpace(defaultsInstance)
                ? defaultsInstance
                : "default";

        ulong versionHash = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(version));
        string versionHex = versionHash.ToString("x16");
        ETagMode etagMode = overlay?.ETagMode
            ?? dom.OutputCache?.ETagMode
            ?? defaults.OutputCache?.ETagMode
            ?? ETagMode.Version;
        StringValues etag = CacheETagFactory.FromVersion(version);

        TimeSpan outputTtl = overlay?.OutputCacheTtl
            ?? Pick(dom.OutputCache?.Ttl, defaults.OutputCache?.Ttl, TimeSpan.FromSeconds(3700));
        TimeSpan dataCacheTtl = overlay?.DataCacheTtl
            ?? Pick(dom.DataCache?.Ttl, defaults.DataCache?.Ttl, TimeSpan.FromSeconds(3800));
        TimeSpan clientTtl = overlay?.ClientTtl
            ?? Pick(dom.ClientCache?.Ttl, defaults.ClientCache?.Ttl, TimeSpan.FromSeconds(3600));
        TimeSpan clientTtlMin = overlay?.ClientTtlMin
            ?? Pick(dom.ClientCache?.TtlMin, defaults.ClientCache?.TtlMin, TimeSpan.FromSeconds(60));

        TimeSpan dataCacheHardTtl = overlay?.HardTtl
            ?? Pick(dom.DataCache?.HardTtl, defaults.DataCache?.HardTtl, TimeSpan.FromSeconds(43200));
        TimeSpan dataCacheFailSafe = overlay?.FailSafe
            ?? Pick(dom.DataCache?.FailSafe, defaults.DataCache?.FailSafe, TimeSpan.FromSeconds(86400));
        TimeSpan dataCacheJitter = overlay?.Jitter
            ?? Pick(dom.DataCache?.Jitter, defaults.DataCache?.Jitter, TimeSpan.FromSeconds(60));
        TimeSpan dataCacheFactorySoft = overlay?.FactorySoftTimeout
            ?? Pick(dom.DataCache?.FactorySoftTimeout, defaults.DataCache?.FactorySoftTimeout, TimeSpan.FromSeconds(1));
        TimeSpan dataCacheFactoryHard = overlay?.FactoryHardTimeout
            ?? Pick(dom.DataCache?.FactoryHardTimeout, defaults.DataCache?.FactoryHardTimeout, TimeSpan.FromSeconds(5));

        AuthBypassMode authBypassMode = ResolveAuthBypassMode(overlay, dom, defaults);

        string[]? acceptNormalization = overlay?.AcceptNormalizationList
            ?? dom.AcceptNormalizationList
            ?? defaults.AcceptNormalizationList
            ?? DefaultAcceptNormalization;

        return new DomainCacheOptions
        {
            Domain = domain,
            DataCacheInstanceName = instanceName,
            OutputCacheEnabled = overlay?.OutputCacheEnabled
                ?? Pick(dom.OutputCache?.Enabled, defaults.OutputCache?.Enabled, true),
            DataCacheEnabled = overlay?.DataCacheEnabled
                ?? Pick(dom.DataCache?.Enabled, defaults.DataCache?.Enabled, true),
            AuthBypassMode = authBypassMode,
            VaryOutputCacheByUser = overlay?.VaryOutputCacheByUser
                ?? Pick(dom.VaryOutputCacheByUser, defaults.VaryOutputCacheByUser, true),
            TreatAuthorizationAsAuthSignal = overlay?.TreatAuthorizationAsAuthSignal
                ?? Pick(dom.TreatAuthorizationAsAuthSignal, defaults.TreatAuthorizationAsAuthSignal, true),
            AuthVaryIncludeAuthorizationHash = overlay?.AuthVaryIncludeAuthorizationHash
                ?? Pick(dom.AuthVaryIncludeAuthorizationHash, defaults.AuthVaryIncludeAuthorizationHash, true),
            DataCacheRespectAuthBypass = overlay?.DataCacheRespectAuthBypass
                ?? Pick(dom.DataCacheRespectAuthBypass, defaults.DataCacheRespectAuthBypass, true),
            ClientForcePrivateWhenAuthenticated = overlay?.ClientForcePrivateWhenAuthenticated
                ?? Pick(
                    dom.ClientCache?.ForcePrivateWhenAuthenticated,
                    defaults.ClientCache?.ForcePrivateWhenAuthenticated,
                    true),
            VaryByAccept = overlay?.VaryByAccept
                ?? Pick(dom.VaryByAccept, defaults.VaryByAccept, true),
            AcceptNormalizationList = acceptNormalization,
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
            CacheableStatusCodes = dom.OutputCache?.CacheableStatusCodes
                ?? defaults.OutputCache?.CacheableStatusCodes
                ?? [200],
            EncodingNormalizationList = dom.OutputCache?.EncodingNormalizationList
                ?? defaults.OutputCache?.EncodingNormalizationList
                ?? ["br", "gzip"],

            ClientCacheability = overlay?.ClientCacheability
                ?? dom.ClientCache?.Cacheability
                ?? defaults.ClientCache?.Cacheability
                ?? ClientCacheability.Public,
            ClientTtlSeconds = ToNonNegSeconds(clientTtl),
            ClientTtlMinSeconds = ToNonNegSeconds(clientTtlMin),
            ScheduledUpdateUtc = overlay?.ScheduledUpdateUtc
                ?? dom.ClientCache?.ScheduledUpdateUtc
                ?? defaults.ClientCache?.ScheduledUpdateUtc,
            ClientMustRevalidateNearUpdate = overlay?.ClientMustRevalidateNearUpdate
                ?? Pick(
                    dom.ClientCache?.MustRevalidateNearUpdate,
                    defaults.ClientCache?.MustRevalidateNearUpdate,
                    false),

            OutputTtl = outputTtl < TimeSpan.Zero ? TimeSpan.Zero : outputTtl,
            DataCacheTtl = dataCacheTtl,
            DataCacheHardTtl = dataCacheHardTtl < TimeSpan.Zero ? TimeSpan.Zero : dataCacheHardTtl,
            DataCacheFailSafe = dataCacheFailSafe < TimeSpan.Zero ? TimeSpan.Zero : dataCacheFailSafe,
            DataCacheEagerRefreshRatio = overlay?.EagerRefreshRatio
                ?? Pick(dom.DataCache?.EagerRefreshRatio, defaults.DataCache?.EagerRefreshRatio, 0.9),
            DataCacheJitter = dataCacheJitter < TimeSpan.Zero ? TimeSpan.Zero : dataCacheJitter,
            DataCacheFactorySoftTimeout = dataCacheFactorySoft < TimeSpan.Zero ? TimeSpan.Zero : dataCacheFactorySoft,
            DataCacheFactoryHardTimeout = dataCacheFactoryHard < TimeSpan.Zero ? TimeSpan.Zero : dataCacheFactoryHard,
            DataCacheMaxItemBytes = overlay?.MaxItemBytes
                ?? Pick(dom.DataCache?.MaxItemBytes, defaults.DataCache?.MaxItemBytes, 0),
            DataCacheAllowBackgroundDistributed = overlay?.AllowBackgroundDistributed
                ?? Pick(dom.DataCache?.AllowBackgroundDistributed, defaults.DataCache?.AllowBackgroundDistributed, true),
            DataCacheAllowBackgroundBackplane = overlay?.AllowBackgroundBackplane
                ?? Pick(dom.DataCache?.AllowBackgroundBackplane, defaults.DataCache?.AllowBackgroundBackplane, true),

            OutputCacheNamespace = options.OutputNamespace,
            DataCacheNamespace = options.DataCacheInstances.TryGetValue(instanceName, out CacheOrchestratorOptions.DataCacheInstanceOptions? inst)
                ? inst.GetNamespace(instanceName, options)
                : new CacheOrchestratorOptions.DataCacheInstanceOptions().GetNamespace(instanceName, options),

            DataCacheRespectNoStore = overlay?.DataCacheRespectNoStore
                ?? Pick(dom.DataCache?.RespectNoStore, defaults.DataCache?.RespectNoStore, true),
            DataCacheVaryOnPublicAddress = overlay?.DataCacheVaryOnPublicAddress
                ?? Pick(dom.DataCache?.VaryOnPublicAddress, defaults.DataCache?.VaryOnPublicAddress, true),
            DataCacheVaryOnEncoding = overlay?.DataCacheVaryOnEncoding
                ?? Pick(dom.DataCache?.VaryOnEncoding, defaults.DataCache?.VaryOnEncoding, true),
            OutputCacheVaryByHost = overlay?.OutputCacheVaryByHost
                ?? Pick(dom.OutputCache?.VaryByHost, defaults.OutputCache?.VaryByHost, true),
        };
    }

    private static int ToNonNegSeconds(TimeSpan value)
    {
        double seconds = value.TotalSeconds;
        if (seconds <= 0)
            return 0;
        if (seconds >= int.MaxValue)
            return int.MaxValue;
        return (int)Math.Round(seconds);
    }

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

        return AuthBypassMode.AuthenticatedOrAuthorization;
    }
}
