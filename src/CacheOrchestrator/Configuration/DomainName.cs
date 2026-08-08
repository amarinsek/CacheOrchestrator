namespace CacheOrchestrator.Configuration;

/// <summary>
/// Normalization helpers for cache domain names and resource ids (public, stable API).
/// </summary>
public static class DomainName
{
    /// <summary>
    /// Domain used when the input is null, empty, or becomes empty after normalization.
    /// </summary>
    public const string Default = "default";

    /// <summary>
    /// Normalizes a domain name into a safe, consistent cache key segment.
    /// </summary>
    /// <param name="s">The raw domain name (may be null, empty, or contain invalid characters).</param>
    /// <returns>
    /// A normalized domain string containing only lowercase letters, digits, and the characters
    /// <c>-</c>, <c>:</c>, <c>_</c>, <c>@</c>. Invalid characters are replaced with a single dash,
    /// consecutive dashes are collapsed, and leading/trailing dashes are removed.
    /// Returns <see cref="Default"/> when the input is null, empty, or becomes empty after normalization.
    /// </returns>
    /// <remarks>
    /// Allocation-conscious single-pass algorithm (no regular expressions) for the hot path.
    /// </remarks>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return Default;

        Span<char> chars = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        s.AsSpan().ToLowerInvariant(chars);
        int write = 0;
        bool lastWasDash = false;

        for (int read = 0; read < chars.Length; read++)
        {
            char c = chars[read];

            // Allowed: a-z, 0-9, -, :, _, @
            bool isAllowed = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or ':' or '_' or '@';

            if (isAllowed)
            {
                if (c == '-')
                {
                    if (lastWasDash)
                        continue;
                    lastWasDash = true;
                }
                else
                {
                    lastWasDash = false;
                }

                chars[write++] = c;
            }
            else if (!lastWasDash)
            {
                chars[write++] = '-';
                lastWasDash = true;
            }
        }

        int start = 0;
        while (start < write && chars[start] == '-')
            start++;

        while (write > start && chars[write - 1] == '-')
            write--;

        return write - start <= 0 ? Default : chars[start..write].ToString();
    }

    /// <summary>
    /// Normalizes a resource id for cache keys and entity tags (same character rules as domains).
    /// </summary>
    /// <param name="resourceId">Raw resource id (e.g. product id from the route).</param>
    /// <returns>Normalized id, or empty string when input is null/whitespace.</returns>
    public static string NormalizeResourceId(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return string.Empty;

        return Normalize(resourceId);
    }
}
