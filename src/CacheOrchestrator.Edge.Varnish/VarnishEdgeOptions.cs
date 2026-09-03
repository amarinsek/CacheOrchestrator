namespace CacheOrchestrator.Edge.Varnish;

internal sealed class VarnishEdgeConfiguration
{
    public Dictionary<string, VarnishEdgeInstanceContainer> EdgeInstances { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class VarnishEdgeInstanceContainer
{
    public VarnishEdgeInstanceOptions? Varnish { get; set; }
}

/// <summary>Varnish invalidation endpoint settings for one named edge instance.</summary>
public sealed class VarnishEdgeInstanceOptions
{
    /// <summary>Absolute URL handled by the protected xkey PURGE route.</summary>
    public string? PurgeUrl { get; set; }

    /// <summary>Optional shared secret sent to the protected PURGE route.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Request header carrying <see cref="ApiKey"/>.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-CacheOrchestrator-Key";
}
