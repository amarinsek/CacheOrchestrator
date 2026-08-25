namespace CacheOrchestrator.Identity;

/// <summary>
/// Stable key/value material folded into Output Cache <c>VaryByValues</c> and data-cache keys.
/// </summary>
public sealed class CacheIdentityMaterial
{
    /// <summary>
    /// Creates material from the given values (copied; ordinal key comparer).
    /// </summary>
    public CacheIdentityMaterial(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Dictionary<string, string> copy = new(StringComparer.Ordinal);
        foreach ((string key, string value) in values)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            copy[key.Trim()] = value ?? string.Empty;
        }

        Values = copy;
    }

    /// <summary>
    /// Creates material from a pre-built dictionary. The dictionary is used as-is (not copied).
    /// Prefer the enumerable constructor for app code.
    /// </summary>
    public CacheIdentityMaterial(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values;
    }

    /// <summary>Named identity segments (must not contain secrets in plaintext).</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>Empty material (still cacheable; identity is empty beyond Url vary).</summary>
    public static CacheIdentityMaterial Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.Ordinal));
}
