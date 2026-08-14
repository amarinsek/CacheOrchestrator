using CacheOrchestrator.Admin;
using CacheOrchestrator.Admin.App.Models;
using CacheOrchestrator.Admin.App.Options;
using CacheOrchestrator.Admin.App.Services.Hints;
using CacheOrchestrator.Invalidation;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Admin.App.Services;

/// <summary>
/// Orchestrates parallel fan-out to Local Admin APIs and aggregates results.
/// </summary>
public sealed class AdminFanOutService
{
    private readonly ILocalAdminClient _client;
    private readonly CacheAdminOptions _options;
    private readonly TimeProvider _time;
    private readonly InstanceReachabilityCache _reachability;
    private readonly HintEngine _hints;

    public AdminFanOutService(
        ILocalAdminClient client,
        IOptions<CacheAdminOptions> options,
        InstanceReachabilityCache reachability,
        HintEngine hints,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(reachability);
        ArgumentNullException.ThrowIfNull(hints);
        _client = client;
        _options = options.Value;
        _reachability = reachability;
        _hints = hints;
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
                Requests = o.Succeeded ? o.Value?.Requests : null
            };
        }).ToArray();
    }

    public async Task<ClusterStatsDto> GetStatsAsync(
        string? scope,
        CancellationToken cancellationToken,
        bool groupByInstance = false,
        string? instances = null)
    {
        IReadOnlyList<AdminInstanceOptions> targets = ResolveInstanceFilter(scope, instances);

        // Stats + domains in parallel; both skip known-down instances (no stacked timeouts).
        Task<List<InstanceCallOutcome<AdminLiveStatsSnapshot>>> statsTask = FanOutAsync(
            targets,
            (inst, ct) => _client.GetStatsAsync(inst, ct),
            cancellationToken,
            skipKnownDown: true);
        Task<List<InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>>> domainsTask = FanOutAsync(
            targets,
            (inst, ct) => _client.GetDomainsAsync(inst, ct),
            cancellationToken,
            skipKnownDown: true);
        await Task.WhenAll(statsTask, domainsTask).ConfigureAwait(false);

        List<InstanceCallOutcome<AdminLiveStatsSnapshot>> outcomes = await statsTask.ConfigureAwait(false);
        RecordDataOutcomes(outcomes);

        List<InstanceStatsContributionDto> contributions = outcomes.Select(o => new InstanceStatsContributionDto
        {
            InstanceId = o.InstanceId,
            Succeeded = o.Succeeded,
            Error = o.Error,
            Snapshot = o.Value
        }).ToList();

        List<AdminLiveStatsSnapshot> ok = outcomes
            .Where(o => o.Succeeded && o.Value is not null)
            .Select(o => o.Value!)
            .ToList();

        // Config for TTL/schedule hints (best-effort from healthy instances).
        Dictionary<string, AdminDomainConfigDto> configByName = new(StringComparer.Ordinal);
        List<InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>>> domainOutcomes =
            await domainsTask.ConfigureAwait(false);
        RecordDataOutcomes(domainOutcomes);
        foreach (AdminDomainConfigDto c in domainOutcomes
                     .Where(o => o.Succeeded && o.Value is not null)
                     .SelectMany(o => o.Value!))
        {
            configByName.TryAdd(c.Name, c);
        }

        IReadOnlyList<AdminDomainStatsDto> domains = StatsAggregator.MergeDomains(ok, groupByInstance)
            .Select(d =>
            {
                configByName.TryGetValue(d.Name, out AdminDomainConfigDto? c);
                return _hints.WithHints(d, c);
            })
            .ToArray();

        IReadOnlyList<AdminEndpointStatsDto> endpoints = StatsAggregator.MergeEndpoints(ok, groupByInstance)
            .Select(_hints.WithHints)
            .ToArray();

        IReadOnlyList<AdminEndpointStatsDto> unassigned = StatsAggregator.MergeUnassignedEndpoints(ok, groupByInstance)
            .Select(_hints.WithHints)
            .ToArray();

        string scopeLabel = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim();
        if (!string.IsNullOrWhiteSpace(instances))
            scopeLabel = "instances:" + string.Join(',', ParseCsv(instances));

        return new ClusterStatsDto
        {
            Scope = scopeLabel,
            GroupByInstance = groupByInstance,
            CollectedAtUtc = _time.GetUtcNow(),
            Instances = contributions,
            Domains = domains,
            Endpoints = endpoints,
            UnassignedEndpoints = unassigned
        };
    }

    public async Task<OverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        // One health pass + one stats pass (with ByInstance). Do not re-fetch stats for hints.
        Task<IReadOnlyList<InstanceStatusDto>> instancesTask = GetInstancesAsync(cancellationToken);
        Task<ClusterStatsDto> statsTask = GetStatsAsync("all", cancellationToken, groupByInstance: true);
        await Task.WhenAll(instancesTask, statsTask).ConfigureAwait(false);

        IReadOnlyList<InstanceStatusDto> instances = await instancesTask.ConfigureAwait(false);
        ClusterStatsDto stats = await statsTask.ConfigureAwait(false);

        long totalRequests = stats.Domains.Sum(d => d.Requests);
        long totalInvalidations = stats.Domains.Sum(d => d.Invalidations);

        // Weighted pipeline from domain sums (rebuild from totals).
        long ocH = stats.Domains.Sum(d => d.Oc.Hits);
        long ocM = stats.Domains.Sum(d => d.Oc.Misses);
        long ocB = stats.Domains.Sum(d => d.Oc.Bypass);
        long fcH = stats.Domains.Sum(d => d.Fc.Hits);
        long fcM = stats.Domains.Sum(d => d.Fc.Misses);
        long fcS = stats.Domains.Sum(d => d.Fc.Stale);
        long fcB = stats.Domains.Sum(d => d.Fc.Bypass);
        long runs = stats.Domains.Sum(d => d.Fc.FactoryRuns);
        long fails = stats.Domains.Sum(d => d.Fc.FactoryFailures);
        (_, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) =
            AdminStatsMath.BuildAll(ocH, ocM, ocB, fcH, fcM, fcS, fcB, runs, fails);

        List<string> alerts = [];
        int down = instances.Count(i => i.Status == InstanceHealthStatus.Down);
        int degraded = instances.Count(i => i.Status == InstanceHealthStatus.Degraded);
        if (down > 0)
            alerts.Add($"{down} instance(s) down.");
        if (degraded > 0)
            alerts.Add($"{degraded} instance(s) degraded.");
        if (instances.Count > 1)
            alerts.Add("Multiple instances: ensure Local Admin fan-out targets all nodes (L1 is per process).");

        // Full lists for Overview: UI sorts by the user's key, then takes top 5.
        // Do not pre-filter here — otherwise re-sort only reshuffles a partial pool.
        IReadOnlyList<AdminDomainStatsDto> topDomains = stats.Domains;
        IReadOnlyList<AdminEndpointStatsDto> topEndpoints = stats.Endpoints;

        IReadOnlyList<AdminHintDto> allHints = HintEngine.CollectFromStats(stats.Domains, stats.Endpoints);
        AdminHintSummaryDto clusterHints = HintEngine.Summarize(allHints);

        // Per-instance hint rollup from ByInstance rows (same stats fetch).
        Dictionary<string, List<AdminHintDto>> instHintLists = new(StringComparer.OrdinalIgnoreCase);
        foreach (AdminDomainStatsDto d in stats.Domains)
        {
            if (d.ByInstance is null)
                continue;
            foreach (AdminDomainStatsDto row in d.ByInstance)
            {
                if (string.IsNullOrEmpty(row.InstanceId))
                    continue;
                if (!instHintLists.TryGetValue(row.InstanceId, out List<AdminHintDto>? list))
                {
                    list = [];
                    instHintLists[row.InstanceId] = list;
                }

                list.AddRange(HintEngine.CollectFromStats([row], row.Endpoints));
            }
        }

        IReadOnlyList<InstanceStatusDto> instancesWithHints = instances.Select(i =>
        {
            instHintLists.TryGetValue(i.Id, out List<AdminHintDto>? hl);
            return new InstanceStatusDto
            {
                Id = i.Id,
                Url = i.Url,
                Status = i.Status,
                ReportedInstanceId = i.ReportedInstanceId,
                Error = i.Error,
                LatencyMs = i.LatencyMs,
                StartedAtUtc = i.StartedAtUtc,
                UptimeSeconds = i.UptimeSeconds,
                Requests = i.Requests,
                HintSummary = hl is null ? new AdminHintSummaryDto() : HintEngine.Summarize(hl)
            };
        }).ToArray();

        IReadOnlyList<AdminHintDto> topHints = allHints
            .OrderByDescending(h => h.Severity switch
            {
                "Critical" => 3,
                "Warning" => 2,
                _ => 1
            })
            .Take(8)
            .ToArray();

        return new OverviewDto
        {
            CollectedAtUtc = _time.GetUtcNow(),
            Instances = instancesWithHints,
            HealthyCount = instances.Count(i => i.Status == InstanceHealthStatus.Healthy),
            DegradedCount = degraded,
            DownCount = down,
            TotalRequests = totalRequests > 0 ? totalRequests : stats.Endpoints.Sum(e => e.Requests),
            TotalInvalidations = totalInvalidations,
            Pipeline = pipeline,
            OcHitShare = oc.HitShare,
            OriginShare = fc.OriginShare,
            Alerts = alerts,
            TopDomains = topDomains,
            TopEndpoints = topEndpoints,
            DomainCount = stats.Domains.Count,
            EndpointCount = stats.Endpoints.Count,
            HintSummary = clusterHints,
            TopHints = topHints
        };
    }

    public async Task<IReadOnlyList<AdminEndpointStatsDto>> GetTopEndpointsAsync(
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
        ClusterStatsDto stats = await GetStatsAsync("all", cancellationToken, groupByInstance, instances)
            .ConfigureAwait(false);
        IEnumerable<AdminEndpointStatsDto> all = stats.Endpoints;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            all = all.Where(e =>
                e.Route.Contains(s, StringComparison.OrdinalIgnoreCase)
                || (e.ConfiguredDomain?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // "__none__" from UI = show empty set
        if (string.Equals(domains, "__none__", StringComparison.OrdinalIgnoreCase)
            || string.Equals(instances, "__none__", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        HashSet<string>? domainFilter = null;
        if (!string.IsNullOrWhiteSpace(domains))
            domainFilter = new HashSet<string>(ParseCsv(domains), StringComparer.OrdinalIgnoreCase);
        else if (!string.IsNullOrWhiteSpace(domain))
            domainFilter = new HashSet<string>([domain.Trim()], StringComparer.OrdinalIgnoreCase);

        if (domainFilter is { Count: > 0 })
        {
            all = all.Where(e =>
                e.ConfiguredDomain is not null
                && domainFilter.Contains(e.ConfiguredDomain));
        }

        if (minRequests > 0)
            all = all.Where(e => e.Requests >= minRequests);

        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(0, skip);
        string sortKey = (sort ?? "originShare").Trim().ToLowerInvariant();

        IOrderedEnumerable<AdminEndpointStatsDto> ordered = sortKey switch
        {
            "hits" or "traffic" or "requests" => all.OrderByDescending(e => e.Requests),
            "route" => all.OrderBy(e => e.Route, StringComparer.OrdinalIgnoreCase),
            "ochitshare" => all.OrderByDescending(e => e.Oc.HitShare ?? -1),
            "ocmissrate" => all.OrderByDescending(e => e.Oc.MissRate ?? -1),
            "fchitshare" => all.OrderByDescending(e => e.Fc.HitShare ?? -1),
            "fcmissshare" => all.OrderByDescending(e => e.Fc.MissShare ?? -1),
            "fcmissrate" or "missrate" => all.OrderByDescending(e => e.Fc.MissRate ?? -1),
            "fchits" => all.OrderByDescending(e => e.Fc.Hits),
            "stale" => all.OrderByDescending(e => e.Fc.Stale),
            _ => all.OrderByDescending(e => e.Fc.OriginShare ?? e.Fc.MissShare ?? -1)
        };

        return ordered.Skip(skip).Take(take).ToArray();
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
                : "No cluster bus detected — Admin App will HTTP fan-out to each target with distribute:false.",
            Instances = probes
        };
    }

    public async Task<FanOutResultDto<object?>> InvalidateAsync(
        AdminAppInvalidateRequest request,
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
        AdminAppVersionRequest request,
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
        AdminAppTtlPatchRequest request,
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
                    $"bus-distribute via origin '{origin.Id}' (Admin App → 1 HTTP call with distribute:true; peers apply via cluster bus).");
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

    private static string NormalizeScopeToTarget(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
            return "all";
        if (scope.StartsWith("instance:", StringComparison.OrdinalIgnoreCase))
            return scope;
        // Allow bare instance id
        return "instance:" + scope.Trim();
    }

    /// <summary>
    /// Combines legacy <paramref name="scope"/> with optional multi-instance CSV filter.
    /// Empty/null <paramref name="instances"/> means all instances from scope.
    /// </summary>
    private IReadOnlyList<AdminInstanceOptions> ResolveInstanceFilter(string? scope, string? instances)
    {
        IReadOnlyList<AdminInstanceOptions> fromScope = ResolveTarget(NormalizeScopeToTarget(scope));
        if (string.IsNullOrWhiteSpace(instances))
            return fromScope;

        if (string.Equals(instances.Trim(), "__none__", StringComparison.OrdinalIgnoreCase))
            return [];

        HashSet<string> wanted = new(ParseCsv(instances), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return fromScope;

        List<AdminInstanceOptions> filtered = fromScope
            .Where(i => wanted.Contains(i.Id))
            .ToList();

        if (filtered.Count == 0)
            throw new KeyNotFoundException($"No configured instances match: {instances}");

        return filtered;
    }

    private static IEnumerable<string> ParseCsv(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0);
}
