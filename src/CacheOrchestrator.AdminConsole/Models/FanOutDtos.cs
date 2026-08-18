namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>How a write operation was delivered to instances.</summary>
public static class DistributionModes
{
    /// <summary>HTTP call to each targeted instance with <c>distribute: false</c>.</summary>
    public const string FanOut = "fan-out";

    /// <summary>Single origin with <c>distribute: true</c>; peers receive via cluster bus.</summary>
    public const string BusDistribute = "bus-distribute";
}

/// <summary>Generic fan-out result wrapper.</summary>
public sealed class FanOutResultDto<T>
{
    /// <summary>Aggregated or primary payload when applicable.</summary>
    public T? Data { get; init; }

    /// <summary>Per-instance outcomes (Admin Console App HTTP targets only).</summary>
    public required IReadOnlyList<InstanceCallResultDto> Results { get; init; }

    /// <summary>
    /// <see cref="DistributionModes.FanOut"/> or <see cref="DistributionModes.BusDistribute"/>.
    /// </summary>
    public string DistributionMode { get; init; } = DistributionModes.FanOut;

    /// <summary>Human-readable summary for UI (how peers were reached).</summary>
    public string? DistributionSummary { get; init; }

    /// <summary>When bus-distribute: the single origin instance id contacted by Admin Console App.</summary>
    public string? BusOriginInstanceId { get; init; }

    /// <summary>Whether Local Admin requests used <c>distribute: true</c>.</summary>
    public bool Distribute { get; init; }

    /// <summary>True when every targeted instance succeeded.</summary>
    public bool AllSucceeded => Results.Count > 0 && Results.All(r => r.Succeeded);

    /// <summary>True when at least one instance succeeded.</summary>
    public bool AnySucceeded => Results.Any(r => r.Succeeded);
}

/// <summary>Cluster bus capability snapshot from Local <c>GET …/cluster/info</c>.</summary>
public sealed class LocalClusterInfoDto
{
    /// <summary>Process instance id.</summary>
    public string? InstanceId { get; set; }

    /// <summary>Cache namespace.</summary>
    public string? Namespace { get; set; }

    /// <summary>Whether the HTTP bus is enabled on that process.</summary>
    public bool BusEnabled { get; set; }

    /// <summary>Membership kind (Null, Static, ServiceDiscovery).</summary>
    public string? Membership { get; set; }

    /// <summary>Known peer count from membership.</summary>
    public int PeerCount { get; set; }
}

/// <summary>Aggregated cluster distribution capability for Admin Console App UI.</summary>
public sealed class ClusterDistributionCapabilityDto
{
    /// <summary>Recommended mode for writes when target is <c>all</c>.</summary>
    public required string RecommendedMode { get; init; }

    /// <summary>UI summary.</summary>
    public required string Summary { get; init; }

    /// <summary>True when at least one healthy instance reports an enabled non-null bus.</summary>
    public bool BusAvailable { get; init; }

    /// <summary>Preferred origin for bus-distribute (first healthy bus-enabled instance).</summary>
    public string? PreferredBusOriginId { get; init; }

    /// <summary>Per-instance cluster probes.</summary>
    public required IReadOnlyList<InstanceClusterProbeDto> Instances { get; init; }
}

/// <summary>One instance's cluster probe for Admin Console App.</summary>
public sealed class InstanceClusterProbeDto
{
    /// <summary>Configured instance id.</summary>
    public required string Id { get; init; }

    /// <summary>Whether the cluster/info call succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Whether bus is enabled on that instance.</summary>
    public bool BusEnabled { get; init; }

    /// <summary>Membership kind when known.</summary>
    public string? Membership { get; set; }

    /// <summary>Peer count when known.</summary>
    public int? PeerCount { get; set; }

    /// <summary>Error when probe failed.</summary>
    public string? Error { get; set; }
}

/// <summary>Outcome of one Local Admin HTTP call.</summary>
public sealed class InstanceCallResultDto
{
    /// <summary>Configured instance id.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Whether the call succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>HTTP status code when a response was received.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Error or timeout message.</summary>
    public string? Error { get; init; }

    /// <summary>Call duration in milliseconds.</summary>
    public double LatencyMs { get; init; }
}
