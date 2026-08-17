using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.Invalidation;

namespace CacheOrchestrator.AdminConsole.Services;

/// <summary>
/// HTTP client for a single instance's Local Admin API.
/// </summary>
public interface ILocalAdminClient
{
    /// <summary>GET health for one instance.</summary>
    Task<InstanceCallOutcome<AdminHealthDto>> GetHealthAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obsolete: instance process-lifetime stats. Prefer Prometheus via Console <c>/api/stats/window</c>.
    /// Unused by Console stats UI (Local Admin <c>/stats</c> remains on instances for diagnostics).
    /// </summary>
    [Obsolete("Prefer Prometheus window stats. Instance /stats counters are not used by Admin Console.")]
    Task<InstanceCallOutcome<AdminLiveStatsRawSnapshot>> GetStatsAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default);

    /// <summary>GET discovered endpoints.</summary>
    Task<InstanceCallOutcome<IReadOnlyList<AdminEndpointInfoDto>>> GetEndpointsAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default);

    /// <summary>GET domain config snapshots.</summary>
    Task<InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>> GetDomainsAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default);

    /// <summary>POST invalidate on one instance.</summary>
    Task<InstanceCallOutcome<CacheInvalidationResult>> InvalidateAsync(
        AdminInstanceOptions instance,
        AdminInvalidateRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>POST version override on one instance.</summary>
    Task<InstanceCallOutcome<AdminDomainMutationResultDto>> SetVersionAsync(
        AdminInstanceOptions instance,
        string domain,
        AdminVersionRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>PATCH TTL override on one instance.</summary>
    Task<InstanceCallOutcome<AdminDomainMutationResultDto>> PatchTtlAsync(
        AdminInstanceOptions instance,
        string domain,
        AdminTtlPatchRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// GET cluster bus info (<c>…/cluster/info</c>). Fails when bus receive endpoints are not mapped.
    /// </summary>
    Task<InstanceCallOutcome<LocalClusterInfoDto>> GetClusterInfoAsync(
        AdminInstanceOptions instance,
        CancellationToken cancellationToken = default);
}

/// <summary>Typed outcome of a Local Admin call including latency metadata.</summary>
public sealed class InstanceCallOutcome<T>
{
    /// <summary>Configured instance id.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Whether the call succeeded and produced a value.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Deserialized payload when succeeded.</summary>
    public T? Value { get; init; }

    /// <summary>HTTP status when a response was received.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Error message when failed.</summary>
    public string? Error { get; init; }

    /// <summary>Elapsed milliseconds.</summary>
    public double LatencyMs { get; init; }

    /// <summary>Maps to the fan-out result DTO (without payload).</summary>
    public InstanceCallResultDto ToResultDto() =>
        new()
        {
            InstanceId = InstanceId,
            Succeeded = Succeeded,
            StatusCode = StatusCode,
            Error = Error,
            LatencyMs = LatencyMs
        };
}
