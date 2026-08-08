namespace CacheOrchestrator.Configuration;

/// <summary>
/// Outcome of the Output Cache layer for a request.
/// </summary>
public enum OutputCacheResult : byte
{
    /// <summary>Response served from Output Cache.</summary>
    Hit = 0,

    /// <summary>Response was generated and is eligible for storage.</summary>
    Miss = 1,

    /// <summary>Output Cache intentionally skipped.</summary>
    Bypass = 2
}