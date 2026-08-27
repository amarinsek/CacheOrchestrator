namespace CacheOrchestrator.Configuration;

/// <summary>
/// Per-request cache outcome stored on the HTTP feature <c>ICacheOrchestratorFeature</c>
/// (AspNetCore package).
/// </summary>
public sealed class CacheDisposition
{
    /// <summary>Output Cache result for this request, if known.</summary>
    public OutputCacheResult? Output { get; set; }

    /// <summary>Data-cache result for this request, if known.</summary>
    public DataCacheResult? Data { get; set; }

    /// <summary>Optional data-cache operation duration in milliseconds.</summary>
    public long? ElapsedMs { get; set; }
}
