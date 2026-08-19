using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace CacheOrchestrator.Utilities;

/// <summary>
/// Utility methods for parsing and manipulating HTTP headers safely.
/// </summary>
internal static class HttpHelper
{
    // Ignore common tracking parameters for generating cache keys to prevent fragmentation.
    // Array + manual loop avoids HashSet/LINQ enumerator overhead on the hot path.
    private static readonly string[] TrackingPrefixes =
    [
        "utm_", "fbclid", "gclid", "msclkid", "ttclid", "_ga", "_gl"
    ];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="key"/> is a known analytics/tracking
    /// query parameter (exact prefix match, case-insensitive).
    /// </summary>
    public static bool IsTrackingParameter(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        string[] prefixes = TrackingPrefixes;
        for (int i = 0; i < prefixes.Length; i++)
        {
            if (key.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when any value in <paramref name="header"/> contains the
    /// Cache-Control directive <paramref name="directive"/> (substring, case-insensitive).
    /// Avoids <see cref="StringValues.ToString"/> allocation when checking a single directive.
    /// </summary>
    public static bool ContainsCacheDirective(StringValues header, string directive)
    {
        if (header.Count == 0 || string.IsNullOrEmpty(directive))
            return false;

        for (int i = 0; i < header.Count; i++)
        {
            string? value = header[i];
            if (value is not null
                && value.Contains(directive, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void ApplyNoCache(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        response.Headers.Pragma = "no-cache";
    }

    public static void NormalizeAcceptEncoding(HttpContext http, string[] allowedEncodings) =>
        NormalizePreferHeader(http.Request.Headers, HeaderNames.AcceptEncoding, allowedEncodings);

    /// <summary>
    /// Collapses <c>Accept</c> to the first matching prefer-list entry (substring, case-insensitive),
    /// or clears it when none match.
    /// </summary>
    public static void NormalizeAccept(HttpContext http, string[] preferredMediaTypes) =>
        NormalizePreferHeader(http.Request.Headers, HeaderNames.Accept, preferredMediaTypes);

    /// <summary>
    /// Collapses <c>Accept-Language</c> to the first matching prefer-list entry (substring, case-insensitive),
    /// or clears it when none match.
    /// </summary>
    public static void NormalizeAcceptLanguage(HttpContext http, string[] preferredLanguages) =>
        NormalizePreferHeader(http.Request.Headers, HeaderNames.AcceptLanguage, preferredLanguages);

    private static void NormalizePreferHeader(
        IHeaderDictionary headers,
        string headerName,
        string[] preferred)
    {
        if (preferred.Length == 0 || !headers.TryGetValue(headerName, out StringValues current) || current.Count == 0)
            return;

        for (int i = 0; i < preferred.Length; i++)
        {
            string item = preferred[i];
            if (string.IsNullOrWhiteSpace(item))
                continue;
            if (ContainsToken(current, item.Trim()))
            {
                headers[headerName] = item.Trim();
                return;
            }
        }

        headers[headerName] = string.Empty;
    }

    private static bool ContainsToken(StringValues header, string token)
    {
        for (int i = 0; i < header.Count; i++)
        {
            string? value = header[i];
            if (value is not null
                && value.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}