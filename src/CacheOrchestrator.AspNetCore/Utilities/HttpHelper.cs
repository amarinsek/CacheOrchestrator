using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace CacheOrchestrator.Utilities;

/// <summary>
/// Utility methods for parsing and manipulating HTTP headers safely.
/// </summary>
internal static class HttpHelper
{
    private static readonly string[] TrackingExact =
    [
        "fbclid", "gclid", "msclkid", "ttclid", "_ga", "_gl"
    ];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="key"/> is a known analytics/tracking
    /// query parameter (case-insensitive).
    /// <c>utm_*</c> is a prefix; <c>_ga</c>/<c>_gl</c> match exactly or as <c>_ga_</c>/<c>_gl_</c>
    /// (GA4 / linker). Click ids are exact so <c>_game</c> is not treated as tracking.
    /// </summary>
    public static bool IsTrackingParameter(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
            return true;

        if (key.StartsWith("_ga_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("_gl_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] exact = TrackingExact;
        for (int i = 0; i < exact.Length; i++)
        {
            if (key.Equals(exact[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when any Cache-Control <paramref name="header"/> value
    /// has a comma-separated directive whose <em>name</em> equals <paramref name="directive"/>
    /// (case-insensitive). <c>s-maxage</c> does not match <c>max-age</c>; <c>no-storey</c>
    /// does not match <c>no-store</c>.
    /// </summary>
    public static bool ContainsCacheDirective(StringValues header, string directive)
    {
        if (header.Count == 0 || string.IsNullOrEmpty(directive))
            return false;

        for (int i = 0; i < header.Count; i++)
        {
            string? value = header[i];
            if (value is not null && HeaderHasDirective(value, directive))
                return true;
        }

        return false;
    }

    private static bool HeaderHasDirective(string value, string directive)
    {
        int start = 0;
        int length = value.Length;
        while (start < length)
        {
            while (start < length && (value[start] is ' ' or '\t' or ','))
                start++;
            if (start >= length)
                break;

            int comma = value.IndexOf(',', start);
            int partEnd = comma < 0 ? length : comma;

            int tokenEnd = start;
            while (tokenEnd < partEnd && value[tokenEnd] is not ('=' or ' ' or '\t'))
                tokenEnd++;

            int tokenLen = tokenEnd - start;
            if (tokenLen == directive.Length
                && string.Compare(value, start, directive, 0, tokenLen, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }

            start = partEnd + 1;
        }

        return false;
    }

    public static void ApplyNoCache(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        response.Headers.Pragma = "no-cache";
    }

    public static void NormalizeAcceptEncoding(HttpContext http, string[] allowedEncodings) =>
        NormalizePreferHeader(
            http.Request.Headers,
            HeaderNames.AcceptEncoding,
            allowedEncodings,
            languageRange: false);

    /// <summary>
    /// Collapses <c>Accept</c> to the first prefer-list media type that appears as a
    /// comma-separated type (parameters after <c>;</c> ignored).
    /// <c>application/json-seq</c> does not match <c>application/json</c>.
    /// Clears the header when none match.
    /// </summary>
    public static void NormalizeAccept(HttpContext http, string[] preferredMediaTypes) =>
        NormalizePreferHeader(
            http.Request.Headers,
            HeaderNames.Accept,
            preferredMediaTypes,
            languageRange: false);

    /// <summary>
    /// Collapses <c>Accept-Language</c> to the first prefer-list tag that matches a
    /// comma-separated language tag (parameters after <c>;</c> ignored).
    /// A prefer tag without a hyphen also matches more specific tags (<c>en</c> → <c>en-US</c>).
    /// Clears the header when none match.
    /// </summary>
    public static void NormalizeAcceptLanguage(HttpContext http, string[] preferredLanguages) =>
        NormalizePreferHeader(
            http.Request.Headers,
            HeaderNames.AcceptLanguage,
            preferredLanguages,
            languageRange: true);

    private static void NormalizePreferHeader(
        IHeaderDictionary headers,
        string headerName,
        string[] preferred,
        bool languageRange)
    {
        if (preferred.Length == 0 || !headers.TryGetValue(headerName, out StringValues current) || current.Count == 0)
            return;

        for (int i = 0; i < preferred.Length; i++)
        {
            string item = preferred[i];
            if (string.IsNullOrWhiteSpace(item))
                continue;
            string trimmed = item.Trim();
            if (HeaderMatchesPrefer(current, trimmed, languageRange))
            {
                headers[headerName] = trimmed;
                return;
            }
        }

        headers[headerName] = string.Empty;
    }

    private static bool HeaderMatchesPrefer(StringValues header, string preferred, bool languageRange)
    {
        for (int i = 0; i < header.Count; i++)
        {
            string? value = header[i];
            if (value is not null && ValueMatchesPrefer(value, preferred, languageRange))
                return true;
        }

        return false;
    }

    private static bool ValueMatchesPrefer(string value, string preferred, bool languageRange)
    {
        int start = 0;
        int length = value.Length;
        while (start < length)
        {
            while (start < length && (value[start] is ' ' or '\t' or ','))
                start++;
            if (start >= length)
                break;

            int comma = value.IndexOf(',', start);
            int partEnd = comma < 0 ? length : comma;

            int tokenEnd = start;
            while (tokenEnd < partEnd && value[tokenEnd] is not (';' or ' ' or '\t'))
                tokenEnd++;

            int tokenLen = tokenEnd - start;
            if (tokenLen > 0
                && PreferTokenEquals(value, start, tokenLen, preferred, languageRange))
            {
                return true;
            }

            start = partEnd + 1;
        }

        return false;
    }

    private static bool PreferTokenEquals(
        string value,
        int start,
        int tokenLen,
        string preferred,
        bool languageRange)
    {
        if (tokenLen == preferred.Length
            && string.Compare(value, start, preferred, 0, tokenLen, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return true;
        }

        if (!languageRange || preferred.Contains('-', StringComparison.Ordinal))
            return false;

        // en matches en-US; does not match ena
        return tokenLen > preferred.Length + 1
            && value[start + preferred.Length] == '-'
            && string.Compare(value, start, preferred, 0, preferred.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
