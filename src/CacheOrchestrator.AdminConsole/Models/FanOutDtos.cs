namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>How a write operation was delivered to instances.</summary>
public static class DistributionModes
{
    /// <summary>HTTP call to each targeted instance with <c>distribute: false</c>.</summary>
    public const string FanOut = "fan-out";

    /// <summary>Single origin with <c>distribute: true</c>; peers receive via cluster bus.</summary>
    public const string BusDistribute = "bus-distribute";
}

/// <summary>Aggregate write completeness for Console APIs.</summary>
public static class WriteOutcomes
{
    /// <summary>Every contacted instance applied the command.</summary>
    public const string Success = "success";

    /// <summary>Some instances applied; others failed or were skipped (cluster may be inconsistent).</summary>
    public const string PartialFailure = "partialFailure";

    /// <summary>No instance applied the command.</summary>
    public const string Failed = "failed";
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

    /// <summary>Whether Admin API requests used <c>distribute: true</c>.</summary>
    public bool Distribute { get; init; }

    /// <summary>
    /// <see cref="WriteOutcomes.Success"/>, <see cref="WriteOutcomes.PartialFailure"/>, or <see cref="WriteOutcomes.Failed"/>.
    /// </summary>
    public string Outcome { get; init; } = WriteOutcomes.Failed;

    /// <summary>Human warning when the write is incomplete.</summary>
    public string? Warning { get; init; }

    /// <summary>Instance ids that did not succeed.</summary>
    public IReadOnlyList<string> FailedInstanceIds { get; init; } = [];

    /// <summary>True when every targeted instance succeeded.</summary>
    public bool AllSucceeded =>
        string.Equals(Outcome, WriteOutcomes.Success, StringComparison.Ordinal)
        || (Results.Count > 0 && Results.All(r => r.Succeeded));

    /// <summary>True when at least one instance succeeded.</summary>
    public bool AnySucceeded => Results.Any(r => r.Succeeded);

    /// <summary>Fills <see cref="Outcome"/>, <see cref="FailedInstanceIds"/>, and <see cref="Warning"/> from <see cref="Results"/>.</summary>
    public FanOutResultDto<T> WithWriteOutcome()
    {
        string[] failed = Results.Where(r => !r.Succeeded).Select(r => r.InstanceId).ToArray();
        string outcome = Results.Count == 0
            ? WriteOutcomes.Failed
            : failed.Length == 0
                ? WriteOutcomes.Success
                : failed.Length == Results.Count
                    ? WriteOutcomes.Failed
                    : WriteOutcomes.PartialFailure;

        string? warning = outcome == WriteOutcomes.Success
            ? null
            : "Cluster write incomplete — one or more instances did not apply the change; cache settings may be inconsistent.";

        return new FanOutResultDto<T>
        {
            Data = Data,
            Results = Results,
            DistributionMode = DistributionMode,
            DistributionSummary = DistributionSummary,
            BusOriginInstanceId = BusOriginInstanceId,
            Distribute = Distribute,
            Outcome = outcome,
            Warning = warning,
            FailedInstanceIds = failed,
        };
    }
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

/// <summary>Outcome of one Admin API call.</summary>
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

/// <summary>Admin API <c>409</c> body when <c>distribute:true</c> peer publish is incomplete.</summary>
public sealed class LocalAdminClusterPublishIncompleteDto
{
    /// <summary>Short error message.</summary>
    public string? Error { get; set; }

    /// <summary>Domain when applicable.</summary>
    public string? Domain { get; set; }

    /// <summary>True when the origin already applied the mutation locally.</summary>
    public bool LocalApplied { get; set; }

    /// <summary>Peers that did not apply.</summary>
    public List<LocalAdminPeerFailureDto>? PeerFailures { get; set; }
}

/// <summary>One peer failure from a Admin API cluster-publish incomplete response.</summary>
public sealed class LocalAdminPeerFailureDto
{
    /// <summary>Membership peer id.</summary>
    public string? PeerId { get; set; }

    /// <summary>Peer error detail.</summary>
    public string? Error { get; set; }
}
