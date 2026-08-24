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

        if (IsNormalized(s))
            return s;

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
    /// Checks if a string is already completely normalized to avoid allocations.
    /// </summary>
    internal static bool IsNormalized(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) return false;
        if (s[0] == '-' || s[^1] == '-') return false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or ':' or '_' or '@') 
                continue;
            
            if (c == '-')
            {
                // Leading dash already rejected; i >= 1 here.
                if (s[i - 1] == '-') return false;
                continue;
            }
            
            return false;
        }

        return true;
    }

    /// <summary>
    /// Normalizes a resource id for cache keys and entity tags (same character rules as domains).
    /// </summary>
    /// <param name="resourceId">Raw resource id (e.g. product id from the route).</param>
    /// <returns>
    /// Normalized id, or empty string when input is null/whitespace, or when the value
    /// contains no usable characters (unlike <see cref="Normalize"/>, this does not fall
    /// back to <see cref="Default"/> — that would collide unrelated ids).
    /// </returns>
    public static string NormalizeResourceId(string? resourceId) => NormalizeKeySegment(resourceId);

    /// <summary>
    /// Normalizes an entity kind (resource type) for cache keys and tags.
    /// Same character rules as <see cref="NormalizeResourceId"/>: garbage such as <c>!!!</c>
    /// becomes empty instead of <see cref="Default"/>, so unrelated kinds do not share a tag.
    /// </summary>
    /// <param name="entityKind">Raw entity kind (e.g. <c>products</c>).</param>
    /// <returns>Normalized kind, or empty string when the value is unusable.</returns>
    public static string NormalizeEntityKind(string? entityKind) => NormalizeKeySegment(entityKind);

    private static string NormalizeKeySegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = Normalize(value);
        if (normalized == Default
            && !string.Equals(value.Trim(), Default, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized;
    }
}
