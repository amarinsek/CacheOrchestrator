namespace CacheOrchestrator.Vary;

/// <summary>
/// Accumulates vary dimensions for Output Cache and/or FusionCache keys.
/// </summary>
/// <remarks>
/// Do not pass raw secrets (Bearer tokens, cookies, API keys) to <see cref="AddValue"/>.
/// Use <see cref="AddHashedValue"/> so the library hashes them before they enter keys or vary dictionaries.
/// </remarks>
public interface ICacheVaryBuilder
{
    /// <summary>
    /// Include a request header in Output Cache <c>HeaderNames</c> and in the Fusion hash (header value).
    /// Sensitive header names are always hashed.
    /// </summary>
    /// <param name="headerName">HTTP header name (case-insensitive).</param>
    void AddHeader(string headerName);

    /// <summary>
    /// Add a named vary value. For Output Cache this becomes a <c>VaryByValues</c> entry;
    /// for Fusion it becomes a hashed key segment. The value must already be non-secret or hashed.
    /// </summary>
    /// <param name="key">Stable vary key (e.g. <c>auth-user</c>).</param>
    /// <param name="value">Non-secret value.</param>
    void AddValue(string key, string value);

    /// <summary>
    /// Hash <paramref name="raw"/> and store under <paramref name="key"/> (never stores the raw secret).
    /// </summary>
    /// <param name="key">Stable vary key.</param>
    /// <param name="raw">Secret or high-entropy material to hash.</param>
    void AddHashedValue(string key, string raw);
}
