namespace CacheOrchestrator.Edge.Cloudflare;

internal sealed class CloudflareEdgeConfiguration
{
    public Dictionary<string, CloudflareEdgeInstanceContainer> EdgeInstances { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CloudflareEdgeInstanceContainer
{
    public CloudflareEdgeInstanceOptions? Cloudflare { get; set; }
}

/// <summary>Cloudflare credentials for one named edge instance.</summary>
public sealed class CloudflareEdgeInstanceOptions
{
    /// <summary>Cloudflare zone identifier.</summary>
    public string? ZoneId { get; set; }

    /// <summary>API token with Cache Purge permission for the zone.</summary>
    public string? ApiToken { get; set; }
}
