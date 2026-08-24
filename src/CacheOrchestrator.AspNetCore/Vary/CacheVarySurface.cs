namespace CacheOrchestrator.Vary;

/// <summary>
/// Which cache surface is consuming vary material (Output Cache vs FusionCache keys).
/// </summary>
public enum CacheVarySurface
{
    /// <summary>ASP.NET Core Output Cache vary rules.</summary>
    OutputCache = 0,

    /// <summary>FusionCache key generation.</summary>
    Fusion = 1,
}
