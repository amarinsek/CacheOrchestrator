namespace CacheOrchestrator.Configuration;

/// <summary>
/// Client-visible cache class used in X-Cache and response header decisions.
/// </summary>
public enum ClientCacheClass : byte
{
    /// <summary>Publicly cacheable response.</summary>
    Public = 0,

    /// <summary>Private (user-specific) cacheable response.</summary>
    Private = 1,

    /// <summary>Must not be stored by clients.</summary>
    NoStore = 2,

    /// <summary>Blocked due to sensitive response content (e.g. Set-Cookie).</summary>
    Blocked = 3
}