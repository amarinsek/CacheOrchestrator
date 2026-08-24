using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace CacheOrchestrator.Vary;

/// <summary>
/// Builds shared <see cref="CacheVaryMaterial"/> from domain options and optional
/// <see cref="ICacheVaryContributor"/> registrations.
/// </summary>
public sealed class CacheVaryMaterializer
{
    /// <summary>Maximum entries allowed in <c>VaryByHeaders</c>.</summary>
    public const int MaxVaryByHeaders = DomainSettingsPatchMapper.MaxVaryByHeaders;

    /// <summary>Maximum entries allowed in <c>VaryByCookies</c>.</summary>
    public const int MaxVaryByCookies = DomainSettingsPatchMapper.MaxVaryByCookies;

    private static readonly string[] SensitiveHeaderNames =
    [
        HeaderNames.Authorization,
        HeaderNames.Cookie,
        "X-Api-Key",
        "X-Auth-Token",
        "Proxy-Authorization",
    ];

    private readonly ICacheVaryContributor[] _contributors;

    /// <summary>Creates a materializer with no contributors (tests / fallback).</summary>
    public CacheVaryMaterializer()
        : this(Array.Empty<ICacheVaryContributor>())
    {
    }

    /// <summary>Creates a materializer that runs <paramref name="contributors"/> after built-in rules.</summary>
    public CacheVaryMaterializer(IEnumerable<ICacheVaryContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        _contributors = contributors.OrderBy(c => c.Order).ToArray();
    }

    /// <summary>
    /// Builds vary material for the given surface. Built-in domain settings run first;
    /// registered contributors run afterward in <see cref="ICacheVaryContributor.Order"/>.
    /// </summary>
    public CacheVaryMaterial Build(HttpContext http, DomainCacheOptions options, CacheVarySurface surface)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        // Accept / Accept-Language prefer-lists (new). Encoding normalization stays OC-policy-only
        // so historical Fusion keys that hashed raw Accept-Encoding remain unchanged.
        if (options.AcceptNormalizationList is { Length: > 0 })
            HttpHelper.NormalizeAccept(http, options.AcceptNormalizationList);
        if (options.AcceptLanguageNormalizationList is { Length: > 0 })
            HttpHelper.NormalizeAcceptLanguage(http, options.AcceptLanguageNormalizationList);

        Builder builder = new(http);

        ApplyBuiltIn(http, options, surface, builder);

        if (_contributors.Length > 0)
        {
            CacheVaryContext context = new()
            {
                HttpContext = http,
                Options = options,
                Surface = surface,
            };
            for (int i = 0; i < _contributors.Length; i++)
                _contributors[i].Contribute(context, builder);
        }

        return builder.ToMaterial();
    }

    private static void ApplyBuiltIn(
        HttpContext http,
        DomainCacheOptions options,
        CacheVarySurface surface,
        Builder builder)
    {
        // Accept-Encoding: OC always varies when present (historical). Fusion when DataCacheVaryOnEncoding.
        StringValues ae = http.Request.Headers.AcceptEncoding;
        if (ae.Count > 0
            && (surface == CacheVarySurface.OutputCache || options.DataCacheVaryOnEncoding))
        {
            builder.AddHeader(HeaderNames.AcceptEncoding);
        }

        if (options.VaryByAccept)
        {
            StringValues accept = http.Request.Headers.Accept;
            if (accept.Count > 0)
                builder.AddHeader(HeaderNames.Accept);
        }

        if (options.VaryByAcceptLanguage)
        {
            StringValues al = http.Request.Headers.AcceptLanguage;
            if (al.Count > 0)
                builder.AddHeader(HeaderNames.AcceptLanguage);
        }

        string[]? extraHeaders = options.VaryByHeaders;
        if (extraHeaders is { Length: > 0 })
        {
            for (int i = 0; i < extraHeaders.Length; i++)
            {
                string name = extraHeaders[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                name = name.Trim();
                if (!http.Request.Headers.ContainsKey(name))
                    continue;
                builder.AddHeader(name);
            }
        }

        string[]? cookies = options.VaryByCookies;
        if (cookies is { Length: > 0 })
        {
            IRequestCookieCollection requestCookies = http.Request.Cookies;
            for (int i = 0; i < cookies.Length; i++)
            {
                string cookieName = cookies[i];
                if (string.IsNullOrWhiteSpace(cookieName))
                    continue;
                cookieName = cookieName.Trim();
                if (!requestCookies.TryGetValue(cookieName, out string? value) || value is null)
                    continue;
                builder.AddHashedValue("cookie:" + cookieName, value);
            }
        }

        bool hasAuthSignal = DomainAuthEvaluator.HasAuthSignal(http, options);
        if (hasAuthSignal && options.VaryOutputCacheByUser && ShouldIncludeAuthUserVary(options, surface))
        {
            string userVary = DomainAuthEvaluator.ResolveAuthenticatedVaryKey(http, options);
            builder.AddValue("auth-user", userVary);
        }

        builder.SetQueryKeys(ResolveQueryKeys(http.Request.Query, options));
    }

    /// <summary>
    /// Output Cache always applies auth-user when varying by user.
    /// Fusion only does so when auth caching is intentional (<see cref="AuthBypassMode.Never"/>)
    /// or when <see cref="DomainCacheOptions.VaryByAuthClaims"/> is configured — preserving
    /// historical Fusion keys under the default auth-bypass modes.
    /// </summary>
    private static bool ShouldIncludeAuthUserVary(DomainCacheOptions options, CacheVarySurface surface)
    {
        if (surface == CacheVarySurface.OutputCache)
            return true;

        if (options.VaryByAuthClaims is { Length: > 0 })
            return true;

        return DomainAuthEvaluator.GetEffectiveAuthBypassMode(options) == AuthBypassMode.Never;
    }

    /// <summary>
    /// Resolves query keys for cache identity.
    /// <see langword="null"/> when no keys matched (treat as empty for consumers);
    /// empty array when allowlist is explicitly empty (no query vary);
    /// otherwise the selected key list.
    /// </summary>
    /// <remarks>
    /// When <see cref="DomainCacheOptions.VaryByQueryKeys"/> is <see langword="null"/>, all non-tracking
    /// keys (minus <see cref="DomainCacheOptions.IgnoreQueryKeys"/>) are returned — historical behaviour.
    /// </remarks>
    public static IReadOnlyList<string> ResolveQueryKeys(IQueryCollection query, DomainCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        string[]? allow = options.VaryByQueryKeys;
        string[]? ignore = options.IgnoreQueryKeys;

        if (allow is { Length: 0 })
            return Array.Empty<string>();

        if (allow is null)
        {
            if (query.Count == 0)
                return Array.Empty<string>();

            List<string> list = new(query.Count);
            foreach (string key in query.Keys)
            {
                if (HttpHelper.IsTrackingParameter(key) || IsIgnoredQueryKey(key, ignore))
                    continue;
                list.Add(key);
            }

            return list;
        }

        List<string> selected = new(allow.Length);
        for (int i = 0; i < allow.Length; i++)
        {
            string key = allow[i];
            if (string.IsNullOrWhiteSpace(key))
                continue;
            key = key.Trim();
            if (!query.ContainsKey(key))
                continue;
            if (HttpHelper.IsTrackingParameter(key) || IsIgnoredQueryKey(key, ignore))
                continue;
            selected.Add(key);
        }

        return selected;
    }

    /// <summary>
    /// Collects query keys as <see cref="StringValues"/> for Output Cache.
    /// </summary>
    public static StringValues CollectQueryKeysForOutputCache(IQueryCollection query, DomainCacheOptions options)
    {
        IReadOnlyList<string> keys = ResolveQueryKeys(query, options);
        if (keys.Count == 0)
            return StringValues.Empty;
        if (keys.Count == 1)
            return new StringValues(keys[0]);
        return new StringValues(keys is string[] arr ? arr : keys.ToArray());
    }

    private static bool IsIgnoredQueryKey(string key, string[]? ignore)
    {
        if (ignore is null || ignore.Length == 0)
            return false;
        for (int i = 0; i < ignore.Length; i++)
        {
            string? item = ignore[i];
            if (string.IsNullOrWhiteSpace(item))
                continue;
            if (string.Equals(key, item.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Returns true when the header value must never appear in plaintext vary material.</summary>
    public static bool IsSensitiveHeader(string headerName)
    {
        if (string.IsNullOrEmpty(headerName))
            return false;
        for (int i = 0; i < SensitiveHeaderNames.Length; i++)
        {
            if (string.Equals(headerName, SensitiveHeaderNames[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Hashes raw material to a stable hex segment.</summary>
    public static string HashSegment(string raw)
    {
        ulong hash = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(raw ?? string.Empty));
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private sealed class Builder : ICacheVaryBuilder
    {
        private readonly HttpContext _http;
        private readonly List<string> _headers = [];
        private readonly HashSet<string> _headerSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly List<string> _responseVary = [];
        private readonly HashSet<string> _responseVarySet = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<string> _queryKeys = Array.Empty<string>();

        public Builder(HttpContext http) => _http = http;

        public void AddHeader(string headerName)
        {
            if (string.IsNullOrWhiteSpace(headerName))
                return;
            headerName = headerName.Trim();

            // Sensitive headers must never enter OC HeaderNames (framework would store raw values).
            if (IsSensitiveHeader(headerName))
            {
                StringValues raw = _http.Request.Headers[headerName];
                if (raw.Count == 0)
                    return;
                AddHashedValue("hdr:" + headerName, raw.ToString());
                return;
            }

            if (!_headerSet.Add(headerName))
                return;

            _headers.Add(headerName);
            if (_responseVarySet.Add(headerName))
                _responseVary.Add(headerName);
        }

        public void AddValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;
            _values[key.Trim()] = value ?? string.Empty;
        }

        public void AddHashedValue(string key, string raw) =>
            AddValue(key, "h:" + HashSegment(raw));

        public void SetQueryKeys(IReadOnlyList<string> keys) => _queryKeys = keys;

        public CacheVaryMaterial ToMaterial() => new()
        {
            HeaderNames = _headers,
            Values = _values,
            QueryKeys = _queryKeys,
            ResponseVaryHeaderNames = _responseVary,
        };
    }
}
