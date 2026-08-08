namespace CacheOrchestrator.Configuration;

/// <summary>
/// Client Cache-Control mode for responses.
/// </summary>
public enum ClientCacheability
{
    /// <summary><c>public, max-age=…</c></summary>
    Public = 0,

    /// <summary><c>private, max-age=…</c></summary>
    Private = 1,

    /// <summary><c>no-store</c> (schedule ignored).</summary>
    NoStore = 2
}