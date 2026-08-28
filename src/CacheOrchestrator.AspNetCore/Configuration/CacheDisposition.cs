namespace CacheOrchestrator.Configuration;

/// <summary>Per-request Output Cache and HTTP Data Cache outcome.</summary>
public sealed class CacheDisposition
{
    /// <summary>Output Cache result for this request, if known.</summary>
    public OutputCacheResult? Output { get; set; }

    /// <summary>HTTP Data Cache result for this request, if known.</summary>
    public DataCacheResult? Data { get; set; }

    /// <summary>Optional Data Cache operation duration in milliseconds.</summary>
    public long? ElapsedMs { get; set; }
}
