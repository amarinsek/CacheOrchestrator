using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AdminConsole.Services;

/// <summary>
/// Orchestrates parallel fan-out to Local Admin APIs and aggregates results.
/// </summary>
public sealed class AdminFanOutService
{
    private readonly ILocalAdminClient _client;
    private readonly AdminConsoleOptions _options;
    private readonly TimeProvider _time;
    private readonly InstanceReachabilityCache _reachability;

    public AdminFanOutService(
        ILocalAdminClient client,
        IOptions<AdminConsoleOptions> options,
        InstanceReachabilityCache reachability,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(reachability);
        _client = client;
        _options = options.Value;
        _reachability = reachability;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Configured instances (copy).</summary>
    public IReadOnlyList<AdminInstanceOptions> GetConfiguredInstances() =>
        _options.Instances
            .Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Url))
            .Select(i => new AdminInstanceOptions { Id = i.Id.Trim(), Url = i.Url.TrimEnd('/') })
            .ToArray();

    /// <summary>Resolves <c>all</c> or <c>instance:{id}</c> to concrete instances.</summary>
    public IReadOnlyList<AdminInstanceOptions> ResolveTarget(string? target)
    {
        IReadOnlyList<AdminInstanceOptions> all = GetConfiguredInstances();
        if (string.IsNullOrWhiteSpace(target) || string.Equals(target, "all", StringComparison.OrdinalIgnoreCase))
            return all;

        const string prefix = "instance:";
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Target must be 'all' or 'instance:{id}'.", nameof(target));

        string id = target[prefix.Length..].Trim();
        AdminInstanceOptions? match = all.FirstOrDefault(i =>
            string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            throw new KeyNotFoundException($"Unknown instance id '{id}'.");

        return [match];
    }

    public async Task<IReadOnlyList<InstanceStatusDto>> GetInstancesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AdminInstanceOptions> instances = GetConfiguredInstances();
        List<InstanceCallOutcome<AdminHealthDto>> outcomes =
            await FanOutAsync(
                    instances,
                    (inst, ct) => _client.GetHealthAsync(inst, ct),
                    cancellationToken,
                    skipKnownDown: true)
                .ConfigureAwait(false);

        return outcomes.Select(o =>
        {
            AdminInstanceOptions cfg = instances.First(i => i.Id == o.InstanceId);
            InstanceHealthStatus status = o.Succeeded
                ? (o.Value?.Healthy == true ? InstanceHealthStatus.Healthy : InstanceHealthStatus.Degraded)
                : InstanceHealthStatus.Down;

            if (o.Succeeded)
            {
                _reachability.RecordHealth(
                    o.InstanceId,
                    status,
                    error: null,
                    o.LatencyMs,
                    o.Value?.InstanceId);
            }
            else if (!IsSkippedDownError(o.Error))
            {
                _reachability.RecordHealth(o.InstanceId, InstanceHealthStatus.Down, o.Error, o.LatencyMs, null);
            }

            return new InstanceStatusDto
            {
                Id = o.InstanceId,
                Url = cfg.Url,
                Status = status,
                ReportedInstanceId = o.Value?.InstanceId,
                Error = o.Error,
                LatencyMs = o.LatencyMs,
                StartedAtUtc = o.Value?.StartedAtUtc,
                UptimeSeconds = o.Succeeded ? o.Value?.UptimeSeconds : null,
                // Process-lifetime health counters are not used for Console traffic (Prometheus window only).
                Requests = null
            };
        }).ToArray();
    }

    /// <summary>
    /// Obsolete: process-lifetime counter fan-out removed.
    /// Use <c>GET /api/stats/window</c> (Prometheus) for traffic stats.
    /// </summary>
    [Obsolete("Use /api/stats/window (Prometheus). Instance /stats counters are not used by Admin Console.")]
    public Task<ClusterStatsDto> GetStatsAsync(
        string? scope,
        CancellationToken cancellationToken,
        bool groupByInstance = false,
        string? instances = null)
    {
        string scopeLabel = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim();
        return Task.FromResult(new ClusterStatsDto
        {
            Scope = scopeLabel,
            GroupByInstance = groupByInstance,
            CollectedAtUtc = _time.GetUtcNow(),
            Instances = [],
            Domains = [],
            Endpoints = [],
            UnassignedEndpoints = []
        });
    }

    /// <summary>
    /// Instance health / connectivity overview only.
    /// Traffic counters and hints come from Prometheus (<c>/api/stats/window</c>) in the SPA.
    /// </summary>
    public async Task<OverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<InstanceStatusDto> instances = await GetInstancesAsync(cancellationToken)
            .ConfigureAwait(false);

        int down = instances.Count(i => i.Status == InstanceHealthStatus.Down);
        int degraded = instances.Count(i => i.Status == InstanceHealthStatus.Degraded);
        List<string> alerts = [];
        if (down > 0)
            alerts.Add($"{down} instance(s) down.");
        if (degraded > 0)
            alerts.Add($"{degraded} instance(s) degraded.");

        return new OverviewDto
        {
            CollectedAtUtc = _time.GetUtcNow(),
            Instances = instances,
            HealthyCount = instances.Count(i => i.Status == InstanceHealthStatus.Healthy),
            DegradedCount = degraded,
            DownCount = down,
            TotalRequests = 0,
            TotalInvalidations = 0,
            Pipeline = new AdminPipelineDto(),
            OcHitShare = null,
            FactoryShare = null,
            Alerts = alerts,
            TopDomains = [],
            TopEndpoints = [],
            DomainCount = 0,
            EndpointCount = 0,
            HintSummary = new AdminHintSummaryDto(),
            TopHints = [],
            Impact = null,
            StatsWindow = "metrics-store",
            ImpactRecent = null,
            RecentWindowLabel = null
        };
    }

    /// <summary>
    /// Obsolete empty list. SPA endpoint traffic comes from <c>GET /api/stats/window</c>.
    /// </summary>
    [Obsolete("Use /api/stats/window (Prometheus).")]
    public Task<IReadOnlyList<AdminEndpointStatsDto>> GetTopEndpointsAsync(
        string? sort,
        int take,
        CancellationToken cancellationToken,
        bool groupByInstance = false,
        string? search = null,
        string? domain = null,
        string? domains = null,
        string? instances = null,
        long minRequests = 0,
        int skip = 0)
    {
        _ = (sort, take, cancellationToken, groupByInstance, search, domain, domains, instances, minRequests, skip);
        return Task.FromResult<IReadOnlyList<AdminEndpointStatsDto>>([]);
    }

    public async Task<FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>>> GetDomainsAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AdminInstanceOptions> instances = GetConfiguredInstances();
        List<InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>> outcomes =
            await FanOutAsync(
                    instances,
                    (inst, ct) => _client.GetDomainsAsync(inst, ct),
                    cancellationToken,
                    skipKnownDown: true)
                .ConfigureAwait(false);
        RecordDataOutcomes(outcomes);

        // Prefer first successful snapshot set (config is expected to match across instances; overlays may differ).
        IReadOnlyList<AdminDomainConfigDto>? data = outcomes
            .FirstOrDefault(o => o.Succeeded && o.Value is not null)?.Value;

        // When multiple instances return domains, merge by name (last write wins for display; callers can drill per instance).
        if (outcomes.Count(o => o.Succeeded && o.Value is not null) > 1)
        {
            Dictionary<string, AdminDomainConfigDto> map = new(StringComparer.Ordinal);
            foreach (InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>> o in outcomes)
            {
                if (o.Value is null)
                    continue;
                foreach (AdminDomainConfigDto d in o.Value)
                    map[d.Name] = d;
            }

            data = map.Values.OrderBy(d => d.Name, StringComparer.Ordinal).ToArray();
        }

        return new FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>>
        {
            Data = data ?? [],
            Results = outcomes.Select(o => o.ToResultDto()).ToArray()
        };
    }

    /// <summary>
    /// Probes <c>/cluster/info</c> on configured instances and recommends fan-out vs bus-distribute.
    /// </summary>
    public async Task<ClusterDistributionCapabilityDto> GetDistributionCapabilityAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AdminInstanceOptions> all = GetConfiguredInstances();
        List<InstanceCallOutcome<LocalClusterInfoDto>> outcomes =
            await FanOutAsync(
                    all,
                    (inst, ct) => _client.GetClusterInfoAsync(inst, ct),
                    cancellationToken,
                    skipKnownDown: true)
                .ConfigureAwait(false);
        // Cluster info may be missing when the bus is off — do not mark instances Down.
        // A successful probe still refreshes reachability as Up.
        foreach (InstanceCallOutcome<LocalClusterInfoDto> o in outcomes)
        {
            if (o.Succeeded && !IsSkippedDownError(o.Error))
                _reachability.RecordSuccess(o.InstanceId, o.Value?.InstanceId, o.LatencyMs);
        }

        List<InstanceClusterProbeDto> probes = outcomes.Select(o =>
        {
            bool busOn = o.Succeeded
                && o.Value is { BusEnabled: true }
                && !string.Equals(o.Value.Membership, "Null", StringComparison.OrdinalIgnoreCase);
            return new InstanceClusterProbeDto
            {
                Id = o.InstanceId,
                Succeeded = o.Succeeded,
                BusEnabled = busOn,
                Membership = o.Value?.Membership,
                PeerCount = o.Succeeded ? o.Value?.PeerCount : null,
                Error = o.Error
            };
        }).ToList();

        InstanceClusterProbeDto? preferred = probes.FirstOrDefault(p => p.BusEnabled);
        bool busAvailable = preferred is not null;

        return new ClusterDistributionCapabilityDto
        {
            BusAvailable = busAvailable,
            PreferredBusOriginId = preferred?.Id,
            RecommendedMode = busAvailable ? DistributionModes.BusDistribute : DistributionModes.FanOut,
            Summary = busAvailable
                ? $"Cluster bus available — prefer single origin ({preferred!.Id}) with distribute:true (peers apply via bus)."
                : "No cluster bus detected — Admin Console App will HTTP fan-out to each target with distribute:false.",
            Instances = probes
        };
    }

    public async Task<FanOutResultDto<object?>> InvalidateAsync(
        AdminConsoleInvalidateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        WriteDistributionPlan plan = await PlanWriteDistributionAsync(request.Target, cancellationToken)
            .ConfigureAwait(false);

        AdminInvalidateRequest body = new()
        {
            Scope = request.Scope,
            Domain = request.Domain,
            EntityKind = request.EntityKind,
            EntityId = request.EntityId,
            Tags = request.Tags,
            Distribute = plan.Distribute
        };

        List<InstanceCallOutcome<CacheInvalidationResult>> outcomes =
            await FanOutAsync(
                    plan.Targets,
                    (inst, ct) => _client.InvalidateAsync(inst, body, ct),
                    cancellationToken,
                    skipKnownDown: true)
                .ConfigureAwait(false);
        RecordDataOutcomes(outcomes);

        return new FanOutResultDto<object?>
        {
            Data = null,
            Results = outcomes.Select(o => o.ToResultDto()).ToArray(),
            DistributionMode = plan.Mode,
            DistributionSummary = plan.Summary,
            BusOriginInstanceId = plan.BusOriginInstanceId,
            Distribute = plan.Distribute
        };
    }

    public async Task<FanOutResultDto<object?>> SetVersionAsync(
        string domain,
        AdminConsoleVersionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(request);
        WriteDistributionPlan plan = await PlanWriteDistributionAsync(request.Target, cancellationToken)
            .ConfigureAwait(false);

        AdminVersionRequest body = new()
        {
            Version = request.Version,
            Distribute = plan.Distribute
        };

        List<InstanceCallOutcome<AdminDomainMutationResultDto>> outcomes =
            await FanOutAsync(
                    plan.Targets,
                    (inst, ct) => _client.SetVersionAsync(inst, domain, body, ct),
                    cancellationToken,
                    skipKnownDown: true)
                .ConfigureAwait(false);
        RecordDataOutcomes(outcomes);

        return new FanOutResultDto<object?>
        {
            Data = outcomes.FirstOrDefault(o => o.Succeeded)?.Value,
            Results = outcomes.Select(o => o.ToResultDto()).ToArray(),
            DistributionMode = plan.Mode,
            DistributionSummary = plan.Summary,
            BusOriginInstanceId = plan.BusOriginInstanceId,
            Distribute = plan.Distribute
        };
    }

    public async Task<FanOutResultDto<object?>> PatchTtlAsync(
        string domain,
        AdminConsoleTtlPatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(request);
        WriteDistributionPlan plan = await PlanWriteDistributionAsync(request.Target, cancellationToken)
            .ConfigureAwait(false);

        AdminTtlPatchRequest body = new()
        {
            OutputCacheTtlSeconds = request.OutputCacheTtlSeconds,
            FusionCacheSoftTtlSeconds = request.FusionCacheSoftTtlSeconds,
            FusionCacheHardTtlSeconds = request.FusionCacheHardTtlSeconds,
            FusionCacheFailSafeSeconds = request.FusionCacheFailSafeSeconds,
            ClientTtlSeconds = request.ClientTtlSeconds,
            ClientTtlMinSeconds = request.ClientTtlMinSeconds,
            Distribute = plan.Distribute
        };

        List<InstanceCallOutcome<AdminDomainMutationResultDto>> outcomes =
            await FanOutAsync(
                    plan.Targets,
                    (inst, ct) => _client.PatchTtlAsync(inst, domain, body, ct),
                    cancellationToken,
                    skipKnownDown: true)
                .ConfigureAwait(false);
        RecordDataOutcomes(outcomes);

        return new FanOutResultDto<object?>
        {
            Data = outcomes.FirstOrDefault(o => o.Succeeded)?.Value,
            Results = outcomes.Select(o => o.ToResultDto()).ToArray(),
            DistributionMode = plan.Mode,
            DistributionSummary = plan.Summary,
            BusOriginInstanceId = plan.BusOriginInstanceId,
            Distribute = plan.Distribute
        };
    }

    /// <summary>
    /// When target is <c>all</c> and a bus-enabled instance exists → single origin + distribute.
    /// Explicit <c>instance:x</c> keeps that target; distribute only if that instance reports bus.
    /// Otherwise classic fan-out with distribute:false.
    /// </summary>
    private async Task<WriteDistributionPlan> PlanWriteDistributionAsync(
        string? target,
        CancellationToken cancellationToken)
    {
        bool targetAll = string.IsNullOrWhiteSpace(target)
            || string.Equals(target, "all", StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<AdminInstanceOptions> explicitTargets = ResolveTarget(target);
        ClusterDistributionCapabilityDto capability =
            await GetDistributionCapabilityAsync(cancellationToken).ConfigureAwait(false);

        if (targetAll && capability.BusAvailable
            && !string.IsNullOrWhiteSpace(capability.PreferredBusOriginId))
        {
            AdminInstanceOptions origin = explicitTargets.First(t =>
                string.Equals(t.Id, capability.PreferredBusOriginId, StringComparison.OrdinalIgnoreCase));

            return new WriteDistributionPlan(
                Targets: [origin],
                Distribute: true,
                Mode: DistributionModes.BusDistribute,
                BusOriginInstanceId: origin.Id,
                Summary:
                    $"bus-distribute via origin '{origin.Id}' (Admin Console App → 1 HTTP call with distribute:true; peers apply via cluster bus).");
        }

        // Explicit single instance: enable distribute only when that instance has a live bus.
        if (!targetAll && explicitTargets.Count == 1)
        {
            InstanceClusterProbeDto? probe = capability.Instances
                .FirstOrDefault(p => string.Equals(p.Id, explicitTargets[0].Id, StringComparison.OrdinalIgnoreCase));
            if (probe is { BusEnabled: true })
            {
                return new WriteDistributionPlan(
                    Targets: explicitTargets,
                    Distribute: true,
                    Mode: DistributionModes.BusDistribute,
                    BusOriginInstanceId: explicitTargets[0].Id,
                    Summary:
                        $"bus-distribute via origin '{explicitTargets[0].Id}' (distribute:true; peers via cluster bus).");
            }
        }

        string ids = string.Join(", ", explicitTargets.Select(t => t.Id));
        return new WriteDistributionPlan(
            Targets: explicitTargets,
            Distribute: false,
            Mode: DistributionModes.FanOut,
            BusOriginInstanceId: null,
            Summary:
                $"fan-out to {explicitTargets.Count} instance(s) [{ids}] with distribute:false (each process applies locally).");
    }

    private sealed record WriteDistributionPlan(
        IReadOnlyList<AdminInstanceOptions> Targets,
        bool Distribute,
        string Mode,
        string? BusOriginInstanceId,
        string Summary);

    /// <summary>
    /// Parallel fan-out. When <paramref name="skipKnownDown"/> is true, instances marked Down
    /// within the re-probe window return a failed outcome immediately (no HTTP / no timeout wait).
    /// </summary>
    private async Task<List<InstanceCallOutcome<T>>> FanOutAsync<T>(
        IReadOnlyList<AdminInstanceOptions> instances,
        Func<AdminInstanceOptions, CancellationToken, Task<InstanceCallOutcome<T>>> call,
        CancellationToken cancellationToken,
        bool skipKnownDown = true)
    {
        if (instances.Count == 0)
            return [];

        int parallelism = Math.Clamp(_options.Parallelism, 1, 64);
        using SemaphoreSlim gate = new(parallelism, parallelism);
        List<Task<InstanceCallOutcome<T>>> tasks = new(instances.Count);

        foreach (AdminInstanceOptions instance in instances)
        {
            if (skipKnownDown && _reachability.ShouldSkipUnreachable(instance.Id))
            {
                CachedInstanceHealth? cached = _reachability.TryGetSkippedDown(instance.Id);
                tasks.Add(Task.FromResult(new InstanceCallOutcome<T>
                {
                    InstanceId = instance.Id,
                    Succeeded = false,
                    Error = cached?.Error is { Length: > 0 } err
                        ? $"Skipped (instance down): {err}"
                        : "Skipped (instance down; will re-probe shortly).",
                    LatencyMs = 0
                }));
                continue;
            }

            tasks.Add(RunOneAsync(instance, call, gate, cancellationToken));
        }

        InstanceCallOutcome<T>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    private static async Task<InstanceCallOutcome<T>> RunOneAsync<T>(
        AdminInstanceOptions instance,
        Func<AdminInstanceOptions, CancellationToken, Task<InstanceCallOutcome<T>>> call,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await call(instance, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private void RecordDataOutcomes<T>(IEnumerable<InstanceCallOutcome<T>> outcomes)
    {
        foreach (InstanceCallOutcome<T> o in outcomes)
        {
            if (IsSkippedDownError(o.Error))
                continue;
            if (o.Succeeded)
                _reachability.RecordSuccess(o.InstanceId, latencyMs: o.LatencyMs);
            else
                _reachability.RecordFailure(o.InstanceId, o.Error, o.LatencyMs);
        }
    }

    private static bool IsSkippedDownError(string? error) =>
        error is not null && error.StartsWith("Skipped (instance down", StringComparison.Ordinal);
}
