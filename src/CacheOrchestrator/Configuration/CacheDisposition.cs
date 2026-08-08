namespace CacheOrchestrator.Configuration;

/// <summary>
/// Per-request cache outcome stored in <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/>.
/// </summary>
public sealed class CacheDisposition
{
    /// <summary>Output Cache result for this request, if known.</summary>
    public OutputCacheResult? Output { get; set; }

    /// <summary>FusionCache (data) result for this request, if known.</summary>
    public DataCacheResult? Data { get; set; }

    /// <summary>Optional FusionCache operation duration in milliseconds.</summary>
    public long? ElapsedMs { get; set; }
}