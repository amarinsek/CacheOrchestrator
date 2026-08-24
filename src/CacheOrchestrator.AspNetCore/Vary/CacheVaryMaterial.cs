namespace CacheOrchestrator.Vary;

/// <summary>
/// Resolved vary dimensions shared by Output Cache and FusionCache key generation.
/// </summary>
public sealed class CacheVaryMaterial
{
    /// <summary>Request header names to vary on (Output Cache <c>HeaderNames</c> / Fusion hash).</summary>
    public IReadOnlyList<string> HeaderNames { get; init; } = [];

    /// <summary>Named vary values (Output Cache <c>VaryByValues</c> / Fusion hash segments).</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Query parameter names included in the cache identity.
    /// Empty means no query vary. When built from null <c>VaryByQueryKeys</c>, contains all non-tracking keys.
    /// </summary>
    public IReadOnlyList<string> QueryKeys { get; init; } = [];

    /// <summary>
    /// Header names safe to advertise on the HTTP response <c>Vary</c> header
    /// (excludes secrets-bearing names such as <c>Authorization</c> / <c>Cookie</c>).
    /// </summary>
    public IReadOnlyList<string> ResponseVaryHeaderNames { get; init; } = [];
}
