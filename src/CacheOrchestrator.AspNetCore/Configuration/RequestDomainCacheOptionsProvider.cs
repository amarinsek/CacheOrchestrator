using CacheOrchestrator.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CacheOrchestrator.Configuration;

/// <summary>
/// Resolves ASP.NET Core domain policy and pins a snapshot on each request.
/// </summary>
internal sealed class RequestDomainCacheOptionsProvider : IRequestDomainCacheOptions, IDisposable
{
    private static readonly string[] DefaultAcceptNormalization = ["application/json", "application/xml"];

    private readonly IDomainCacheOptionsProvider _coreOptions;
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _optionsMonitor;
    private readonly IDomainRuntimeOverrideStore _runtimeOverrides;
    private readonly ILogger<RequestDomainCacheOptionsProvider> _logger;
    private readonly ConcurrentDictionary<string, CachedHttpOptions> _globalCache = new(StringComparer.Ordinal);
    private readonly IDisposable? _changeRegistration;

    public RequestDomainCacheOptionsProvider(
        IDomainCacheOptionsProvider coreOptions,
        IOptionsMonitor<CacheOrchestratorOptions> optionsMonitor,
        ILogger<RequestDomainCacheOptionsProvider> logger,
        IDomainRuntimeOverrideStore? runtimeOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(coreOptions);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _coreOptions = coreOptions;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _runtimeOverrides = runtimeOverrides ?? NullDomainRuntimeOverrideStore.Instance;
        _changeRegistration = _optionsMonitor.OnChange(_ => _globalCache.Clear());
    }

    private sealed class CachedHttpOptions
    {
        public required DomainHttpCacheOptions Options { get; init; }
        public int OverrideStamp { get; init; }
    }

    public void Dispose() => _changeRegistration?.Dispose();

    public DomainHttpCacheOptions GetOrCreateDomainOptions(string domain)
    {
        string normalized = DomainName.Normalize(domain);
        int stamp = _runtimeOverrides.GetStamp(normalized);

        if (_globalCache.TryGetValue(normalized, out CachedHttpOptions? cached)
            && cached.OverrideStamp == stamp)
        {
            return cached.Options;
        }

        DomainHttpCacheOptions options = CreateDomainOptions(normalized);
        _globalCache[normalized] = new CachedHttpOptions { Options = options, OverrideStamp = stamp };
        return options;
    }

    public DomainHttpCacheOptions EnsureDomainOptions(HttpContext http, string domain)
    {
        ArgumentNullException.ThrowIfNull(http);

        string normalized = DomainName.Normalize(domain);
        ICacheOrchestratorFeature feature = CacheOrchestratorFeatureAccessor.GetOrCreate(http);

        if (feature.DomainOptions is { } cached)
        {
            if (string.Equals(cached.Domain, normalized, StringComparison.Ordinal))
                return cached;

            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "Replacing request domain snapshot '{PreviousDomain}' with '{Domain}'.",
                    cached.Domain,
                    normalized);
            }
        }

        DomainHttpCacheOptions resolved = GetOrCreateDomainOptions(normalized);
        feature.DomainOptions = resolved;
        return resolved;
    }

    public DomainHttpCacheOptions? GetDomainOptions(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Features.Get<ICacheOrchestratorFeature>()?.DomainOptions;
    }

    private DomainHttpCacheOptions CreateDomainOptions(string domain)
    {
        DomainCacheOptions core = _coreOptions.GetOrCreateDomainOptions(domain);
        CacheOrchestratorOptions options = _optionsMonitor.CurrentValue;
        CacheOrchestratorOptions.DomainCacheSettings defaults = options.DomainDefaults;
        DomainRuntimeOverride? overlay = _runtimeOverrides.Get(domain);

        if (!options.Domains.TryGetValue(domain, out CacheOrchestratorOptions.DomainCacheSettings? domainSettings))
            domainSettings = new CacheOrchestratorOptions.DomainCacheSettings();

        static T Pick<T>(T? specific, T? global, T fallback) where T : struct =>
            specific ?? global ?? fallback;

        TimeSpan outputTtl = overlay?.OutputCacheTtl
            ?? Seconds(Pick(domainSettings.OutputCache?.TtlSeconds, defaults.OutputCache?.TtlSeconds, 3700));
        TimeSpan clientTtl = overlay?.ClientTtl
            ?? Seconds(Pick(domainSettings.ClientCache?.TtlSeconds, defaults.ClientCache?.TtlSeconds, 3600));
        TimeSpan clientTtlMin = overlay?.ClientTtlMin
            ?? Seconds(Pick(domainSettings.ClientCache?.TtlMinSeconds, defaults.ClientCache?.TtlMinSeconds, 60));

        return new DomainHttpCacheOptions
        {
            CoreOptions = core,
            OutputCacheEnabled = overlay?.OutputCacheEnabled
                ?? Pick(domainSettings.OutputCache?.Enabled, defaults.OutputCache?.Enabled, true),
            AuthBypassMode = ResolveAuthBypassMode(overlay, domainSettings, defaults),
            VaryOutputCacheByUser = overlay?.VaryOutputCacheByUser
                ?? Pick(domainSettings.VaryOutputCacheByUser, defaults.VaryOutputCacheByUser, true),
            TreatAuthorizationAsAuthSignal = overlay?.TreatAuthorizationAsAuthSignal
                ?? Pick(domainSettings.TreatAuthorizationAsAuthSignal, defaults.TreatAuthorizationAsAuthSignal, true),
            AuthVaryIncludeAuthorizationHash = overlay?.AuthVaryIncludeAuthorizationHash
                ?? Pick(domainSettings.AuthVaryIncludeAuthorizationHash, defaults.AuthVaryIncludeAuthorizationHash, true),
            VaryByAuthClaims = overlay?.VaryByAuthClaims
                ?? domainSettings.VaryByAuthClaims
                ?? defaults.VaryByAuthClaims,
            DataCacheRespectAuthBypass = overlay?.DataCacheRespectAuthBypass
                ?? Pick(domainSettings.DataCacheRespectAuthBypass, defaults.DataCacheRespectAuthBypass, true),
            ClientForcePrivateWhenAuthenticated = overlay?.ClientForcePrivateWhenAuthenticated
                ?? Pick(
                    domainSettings.ClientCache?.ForcePrivateWhenAuthenticated,
                    defaults.ClientCache?.ForcePrivateWhenAuthenticated,
                    true),
            VaryByAccept = overlay?.VaryByAccept
                ?? Pick(domainSettings.VaryByAccept, defaults.VaryByAccept, true),
            AcceptNormalizationList = overlay?.AcceptNormalizationList
                ?? domainSettings.AcceptNormalizationList
                ?? defaults.AcceptNormalizationList
                ?? DefaultAcceptNormalization,
            VaryByAcceptLanguage = overlay?.VaryByAcceptLanguage
                ?? Pick(domainSettings.VaryByAcceptLanguage, defaults.VaryByAcceptLanguage, false),
            AcceptLanguageNormalizationList = overlay?.AcceptLanguageNormalizationList
                ?? domainSettings.AcceptLanguageNormalizationList
                ?? defaults.AcceptLanguageNormalizationList,
            VaryByHeaders = overlay?.VaryByHeaders
                ?? domainSettings.VaryByHeaders
                ?? defaults.VaryByHeaders,
            VaryByQueryKeys = overlay?.VaryByQueryKeys
                ?? domainSettings.VaryByQueryKeys
                ?? defaults.VaryByQueryKeys,
            IgnoreQueryKeys = overlay?.IgnoreQueryKeys
                ?? domainSettings.IgnoreQueryKeys
                ?? defaults.IgnoreQueryKeys,
            VaryByCookies = overlay?.VaryByCookies
                ?? domainSettings.VaryByCookies
                ?? defaults.VaryByCookies,
            EmitResponseVary = overlay?.EmitResponseVary
                ?? Pick(domainSettings.EmitResponseVary, defaults.EmitResponseVary, true),
            ETagMode = overlay?.ETagMode
                ?? domainSettings.OutputCache?.ETagMode
                ?? defaults.OutputCache?.ETagMode
                ?? ETagMode.Version,
            ETag = CacheETagFactory.FromVersion(core.Version),
            CacheableStatusCodes = domainSettings.OutputCache?.CacheableStatusCodes
                ?? defaults.OutputCache?.CacheableStatusCodes
                ?? [200],
            EncodingNormalizationList = domainSettings.OutputCache?.EncodingNormalizationList
                ?? defaults.OutputCache?.EncodingNormalizationList
                ?? ["br", "gzip"],
            ClientCacheability = overlay?.ClientCacheability
                ?? domainSettings.ClientCache?.Cacheability
                ?? defaults.ClientCache?.Cacheability
                ?? ClientCacheability.Public,
            ClientTtlSeconds = ToNonNegativeSeconds(clientTtl),
            ClientTtlMinSeconds = ToNonNegativeSeconds(clientTtlMin),
            ScheduledUpdateUtc = overlay?.ScheduledUpdateUtc
                ?? domainSettings.ClientCache?.ScheduledUpdateUtc
                ?? defaults.ClientCache?.ScheduledUpdateUtc,
            ClientMustRevalidateNearUpdate = overlay?.ClientMustRevalidateNearUpdate
                ?? Pick(
                    domainSettings.ClientCache?.MustRevalidateNearUpdate,
                    defaults.ClientCache?.MustRevalidateNearUpdate,
                    false),
            OutputTtl = outputTtl < TimeSpan.Zero ? TimeSpan.Zero : outputTtl,
            OutputCacheNamespace = options.OutputNamespace,
            DataCacheRespectNoStore = overlay?.DataCacheRespectNoStore
                ?? Pick(domainSettings.DataCache?.RespectNoStore, defaults.DataCache?.RespectNoStore, true),
            DataCacheVaryOnPublicAddress = overlay?.DataCacheVaryOnPublicAddress
                ?? Pick(domainSettings.DataCache?.VaryOnPublicAddress, defaults.DataCache?.VaryOnPublicAddress, true),
            DataCacheVaryOnEncoding = overlay?.DataCacheVaryOnEncoding
                ?? Pick(domainSettings.DataCache?.VaryOnEncoding, defaults.DataCache?.VaryOnEncoding, true),
            OutputCacheVaryByHost = overlay?.OutputCacheVaryByHost
                ?? Pick(domainSettings.OutputCache?.VaryByHost, defaults.OutputCache?.VaryByHost, true),
        };
    }

    private static TimeSpan Seconds(int value) =>
        TimeSpan.FromSeconds(value < 0 ? 0 : value);

    private static int ToNonNegativeSeconds(TimeSpan value)
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
        CacheOrchestratorOptions.DomainCacheSettings domain,
        CacheOrchestratorOptions.DomainCacheSettings defaults) =>
        overlay?.AuthBypassMode
        ?? domain.AuthBypassMode
        ?? defaults.AuthBypassMode
        ?? AuthBypassMode.AuthenticatedOrAuthorization;
}
