namespace CacheOrchestrator.Configuration;

/// <summary>Client-visible cache class used in X-Cache and response header decisions.</summary>
public enum ClientCacheClass : byte
{
    /// <summary>Publicly cacheable response.</summary>
    Public = 0,

    /// <summary>Private user-specific response.</summary>
    Private = 1,

    /// <summary>Response must not be stored by clients.</summary>
    NoStore = 2,

    /// <summary>Client caching blocked by response content.</summary>
    Blocked = 3
}
