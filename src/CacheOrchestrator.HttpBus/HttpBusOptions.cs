namespace CacheOrchestrator.HttpBus;

internal sealed class HttpBusOptions
{
    public HttpBusAdminOptions Admin { get; set; } = new();
    public HttpBusClusterOptions Cluster { get; set; } = new();
}

internal sealed class HttpBusAdminOptions
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string RoutePrefix { get; set; } = "/cache-admin/local";
}

internal sealed class HttpBusClusterOptions
{
    public HttpBusTransportOptions Bus { get; set; } = new();
}

internal sealed class HttpBusTransportOptions
{
    public bool Enabled { get; set; }
    public int PeerTimeoutMs { get; set; } = 2000;
    public int MaxParallelism { get; set; } = 32;
    public string Membership { get; set; } = "Null";
    public string? ApiKey { get; set; }
    public HttpBusStaticMembershipOptions Static { get; set; } = new();
    public HttpBusServiceDiscoveryOptions ServiceDiscovery { get; set; } = new();
}

internal sealed class HttpBusStaticMembershipOptions
{
    public List<HttpBusStaticPeerOptions> Instances { get; set; } = [];
}

internal sealed class HttpBusStaticPeerOptions
{
    public string? Id { get; set; }
    public string? Url { get; set; }
}

internal sealed class HttpBusServiceDiscoveryOptions
{
    public string? ServiceName { get; set; }
    public string DefaultScheme { get; set; } = "http";
    public int CacheSeconds { get; set; } = 15;
}
