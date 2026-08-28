namespace CacheOrchestrator.Configuration;

/// <summary>Outcome of Output Cache handling for a request.</summary>
public enum OutputCacheResult : byte
{
    /// <summary>Response served from Output Cache.</summary>
    Hit = 0,

    /// <summary>Response generated and eligible for storage.</summary>
    Miss = 1,

    /// <summary>Output Cache intentionally skipped.</summary>
    Bypass = 2,

    /// <summary>Output Cache disabled for the domain.</summary>
    Off = 3
}
