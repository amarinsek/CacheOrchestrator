namespace CacheOrchestrator.Configuration;

/// <summary>HTTP Cache-Control visibility emitted to clients.</summary>
public enum ClientCacheability
{
    /// <summary>Shared and private caches may store the response.</summary>
    Public = 0,

    /// <summary>Only a private client cache may store the response.</summary>
    Private = 1,

    /// <summary>The response must not be stored.</summary>
    NoStore = 2
}
