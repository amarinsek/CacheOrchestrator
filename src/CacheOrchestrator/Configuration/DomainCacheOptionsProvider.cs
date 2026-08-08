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
/// </remarks>
internal sealed class DomainCacheOptionsProvider : IDomainCacheOptionsProvider, IDisposable
{
    private readonly ILogger<DomainCacheOptionsProvider> _logger;
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, DomainCacheOptions> _globalCache = new(StringComparer.Ordinal);
    private readonly IDisposable? _changeRegistration;

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainCacheOptionsProvider"/> class.
    /// </summary>
    public DomainCacheOptionsProvider(
        IOptionsMonitor<CacheOrchestratorOptions> optionsMonitor,
        ILogger<DomainCacheOptionsProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _optionsMonitor = optionsMonitor;
        _logger = logger;

        _changeRegistration = _optionsMonitor.OnChange(_ =>
        {
            _logger.LogInformation(
                "Configuration changed: clearing global config cache (purged {Count} items).",
                _globalCache.Count);
            _globalCache.Clear();
        });
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
        if (!_globalCache.TryGetValue(domain, out DomainCacheOptions? options))
        {
            options = CreateDomainOptions(domain);
            _globalCache.TryAdd(domain, options);
        }

        return options;
    }

    private DomainCacheOptions CreateDomainOptions(string domain)
    {
        CacheOrchestratorOptions options = _optionsMonitor.CurrentValue;
        CacheOrchestratorOptions.DomainCacheSettings defaults = options.DomainDefaults;

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

        if (!string.IsNullOrWhiteSpace(dom.Version))
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
        StringValues etag = CacheETagFactory.FromVersion(version);
        ETagMode etagMode = dom.ETagMode ?? defaults.ETagMode ?? ETagMode.Version;

        return new DomainCacheOptions
        {
            Domain = domain,
            FusionCacheInstanceName = instanceName,
            OutputCacheEnabled = Pick(dom.OutputCacheEnabled, defaults.OutputCacheEnabled, true),
            FusionCacheEnabled = Pick(dom.FusionCacheEnabled, defaults.FusionCacheEnabled, true),
            BypassWhenAuthenticated = Pick(dom.BypassWhenAuthenticated, defaults.BypassWhenAuthenticated, true),
            VaryOutputCacheByUser = Pick(dom.VaryOutputCacheByUser, defaults.VaryOutputCacheByUser, true),
            Version = version,
            VersionHex = versionHex,
            ETagMode = etagMode,
            ETag = etag,
            CacheableStatusCodes = dom.CacheableStatusCodes ?? defaults.CacheableStatusCodes ?? [200],
            EncodingNormalizationList = dom.EncodingNormalizationList ?? defaults.EncodingNormalizationList,

            ClientCacheability = dom.ClientCacheability ?? defaults.ClientCacheability ?? ClientCacheability.Public,
            ClientTtlSeconds = Pick(dom.ClientTtlSeconds, defaults.ClientTtlSeconds, 3600),
            ClientTtlMinSeconds = Pick(dom.ClientTtlMinSeconds, defaults.ClientTtlMinSeconds, 60),
            ScheduledUpdateUtc = dom.ScheduledUpdateUtc ?? defaults.ScheduledUpdateUtc,
            ClientMustRevalidateNearUpdate = Pick(dom.ClientMustRevalidateNearUpdate, defaults.ClientMustRevalidateNearUpdate, false),

            OutputTtl = TimeSpan.FromSeconds(Math.Max(0, Pick(dom.OutputCacheTtlSeconds, defaults.OutputCacheTtlSeconds, 3700))),
            FusionCacheSoftTtl = TimeSpan.FromSeconds(Pick(dom.FusionCacheSoftTtlSeconds, defaults.FusionCacheSoftTtlSeconds, 3800)),
            FusionCacheHardTtl = TimeSpan.FromSeconds(Pick(dom.FusionCacheHardTtlSeconds, defaults.FusionCacheHardTtlSeconds, 43200)),
            FusionCacheFailSafe = TimeSpan.FromSeconds(Pick(dom.FusionCacheFailSafeSeconds, defaults.FusionCacheFailSafeSeconds, 86400)),

            OutputCacheNamespace = options.OutputNamespace,
            FusionCacheNamespace = options.FusionCacheInstances.TryGetValue(instanceName, out CacheOrchestratorOptions.FusionCacheInstanceOptions? inst)
                ? inst.GetNamespace(instanceName, options)
                : new CacheOrchestratorOptions.FusionCacheInstanceOptions().GetNamespace(instanceName, options),

            FusionCacheEagerRefreshRatio = Pick(dom.FusionCacheEagerRefreshRatio, defaults.FusionCacheEagerRefreshRatio, 0.9),
            FusionCacheJitterSeconds = Pick(dom.FusionCacheJitterSeconds, defaults.FusionCacheJitterSeconds, 60),
            FusionCacheFactorySoftTimeoutSeconds = Pick(dom.FusionCacheFactorySoftTimeoutSeconds, defaults.FusionCacheFactorySoftTimeoutSeconds, 1),
            FusionCacheFactoryHardTimeoutSeconds = Pick(dom.FusionCacheFactoryHardTimeoutSeconds, defaults.FusionCacheFactoryHardTimeoutSeconds, 5),
            FusionCacheMaxItemBytes = Pick(dom.FusionCacheMaxItemBytes, defaults.FusionCacheMaxItemBytes, 0),
            FusionCacheRespectNoStore = Pick(dom.FusionCacheRespectNoStore, defaults.FusionCacheRespectNoStore, true),
            FusionCacheAllowBackgroundDistributed = Pick(dom.FusionCacheAllowBackgroundDistributed, defaults.FusionCacheAllowBackgroundDistributed, true),
            FusionCacheAllowBackgroundBackplane = Pick(dom.FusionCacheAllowBackgroundBackplane, defaults.FusionCacheAllowBackgroundBackplane, true),
            FusionCacheVaryOnPublicAddress = Pick(dom.FusionCacheVaryOnPublicAddress, defaults.FusionCacheVaryOnPublicAddress, true),
            FusionCacheVaryOnEncoding = Pick(dom.FusionCacheVaryOnEncoding, defaults.FusionCacheVaryOnEncoding, true),
        };
    }
}
