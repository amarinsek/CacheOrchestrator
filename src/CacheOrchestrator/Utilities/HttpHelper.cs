using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

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

    public static void NormalizeAcceptEncoding(HttpContext http, string[] allowedEncodings)
    {
        StringValues ae = http.Request.Headers.AcceptEncoding;
        if (ae.Count == 0)
            return;

        // Prefer scanning individual values before falling back to a combined string.
        for (int i = 0; i < allowedEncodings.Length; i++)
        {
            string enc = allowedEncodings[i];
            if (ContainsEncoding(ae, enc))
            {
                http.Request.Headers.AcceptEncoding = enc;
                return;
            }
        }

        // If no match found and we mandate normalization, we clear it (identity)
        http.Request.Headers.AcceptEncoding = string.Empty;
    }

    private static bool ContainsEncoding(StringValues header, string encoding)
    {
        for (int i = 0; i < header.Count; i++)
        {
            string? value = header[i];
            if (value is not null
                && value.Contains(encoding, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}