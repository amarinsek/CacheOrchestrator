namespace CacheOrchestrator.Configuration;

/// <summary>Controls generation of the HTTP ETag response header.</summary>
public enum ETagMode
{
    /// <summary>Build the ETag from the domain version.</summary>
    Version = 0,

    /// <summary>Do not emit an ETag.</summary>
    None = 1,

    /// <summary>Build the ETag from the domain version and resource identity.</summary>
    Resource = 2
}
