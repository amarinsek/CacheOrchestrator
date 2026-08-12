using CacheOrchestrator.Admin;
using CacheOrchestrator.Admin.App.Models;
using CacheOrchestrator.Admin.App.Options;
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

    public AdminFanOutService(
        ILocalAdminClient client,
        IOptions<CacheAdminOptions> options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options.Value;
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
            await FanOutAsync(instances, (inst, ct) => _client.GetHealthAsync(inst, ct), cancellationToken)
                .ConfigureAwait(false);

        return outcomes.Select(o =>
        {
            AdminInstanceOptions cfg = instances.First(i => i.Id == o.InstanceId);
            InstanceHealthStatus status = o.Succeeded
                ? (o.Value?.Healthy == true ? InstanceHealthStatus.Healthy : InstanceHealthStatus.Degraded)
                : InstanceHealthStatus.Down;

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
        List<InstanceCallOutcome<AdminLiveStatsSnapshot>> outcomes =
            await FanOutAsync(targets, (inst, ct) => _client.GetStatsAsync(inst, ct), cancellationToken)
                .ConfigureAwait(false);

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

        // Config for TTL/schedule hints (best-effort from first healthy instance set).
        Dictionary<string, AdminDomainConfigDto> configByName = new(StringComparer.Ordinal);
        try
        {
            FanOutResultDto<IReadOnlyList<AdminDomainConfigDto>> cfg = await GetDomainsAsync(cancellationToken)
                .ConfigureAwait(false);
            if (cfg.Data is not null)
            {
                foreach (AdminDomainConfigDto c in cfg.Data)
                    configByName[c.Name] = c;
            }
        }
        catch
        {
            // Hints without config still apply rate-based rules.
        }

        IReadOnlyList<AdminDomainStatsDto> domains = StatsAggregator.MergeDomains(ok, groupByInstance)
            .Select(d =>
            {
                configByName.TryGetValue(d.Name, out AdminDomainConfigDto? c);
                return RecommendationHints.WithHints(d, c);
            })
            .ToArray();

        IReadOnlyList<AdminEndpointStatsDto> endpoints = StatsAggregator.MergeEndpoints(ok, groupByInstance)
            .Select(RecommendationHints.WithHints)
            .ToArray();

        IReadOnlyList<AdminEndpointStatsDto> unassigned = StatsAggregator.MergeUnassignedEndpoints(ok, groupByInstance)
            .Select(RecommendationHints.WithHints)
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
        Task<IReadOnlyList<InstanceStatusDto>> instancesTask = GetInstancesAsync(cancellationToken);
        Task<ClusterStatsDto> statsTask = GetStatsAsync("all", cancellationToken, groupByInstance: false);
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

        IReadOnlyList<AdminEndpointStatsDto> top = stats.Endpoints
            .OrderByDescending(e => e.Fc.OriginShare ?? e.Requests)
            .Take(10)
            .ToArray();

        IReadOnlyList<AdminHintDto> allHints = RecommendationHints.CollectFromStats(stats.Domains, stats.Endpoints);
        AdminHintSummaryDto clusterHints = RecommendationHints.Summarize(allHints);

        // Per-instance hint rollup (groupByInstance domain rows).
        ClusterStatsDto byInst = await GetStatsAsync("all", cancellationToken, groupByInstance: true)
            .ConfigureAwait(false);
        Dictionary<string, List<AdminHintDto>> instHintLists = new(StringComparer.OrdinalIgnoreCase);
        foreach (AdminDomainStatsDto d in byInst.Domains)
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

                list.AddRange(RecommendationHints.CollectFromStats([row], row.Endpoints));
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
                HintSummary = hl is null ? new AdminHintSummaryDto() : RecommendationHints.Summarize(hl)
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
            TopEndpoints = top,
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
            await FanOutAsync(instances, (inst, ct) => _client.GetDomainsAsync(inst, ct), cancellationToken)
                .ConfigureAwait(false);

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

    public async Task<FanOutResultDto<object?>> InvalidateAsync(
        AdminAppInvalidateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<AdminInstanceOptions> targets = ResolveTarget(request.Target);
        AdminInvalidateRequest body = new()
        {
            Scope = request.Scope,
            Domain = request.Domain,
            EntityId = request.EntityId,
            Tags = request.Tags
        };

        List<InstanceCallOutcome<CacheInvalidationResult>> outcomes =
            await FanOutAsync(targets, (inst, ct) => _client.InvalidateAsync(inst, body, ct), cancellationToken)
                .ConfigureAwait(false);

        return new FanOutResultDto<object?>
        {
            Data = null,
            Results = outcomes.Select(o => o.ToResultDto()).ToArray()
        };
    }

    public async Task<FanOutResultDto<object?>> SetVersionAsync(
        string domain,
        AdminAppVersionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<AdminInstanceOptions> targets = ResolveTarget(request.Target);
        AdminVersionRequest body = new() { Version = request.Version };

        List<InstanceCallOutcome<AdminDomainMutationResultDto>> outcomes =
            await FanOutAsync(
                    targets,
                    (inst, ct) => _client.SetVersionAsync(inst, domain, body, ct),
                    cancellationToken)
                .ConfigureAwait(false);

        return new FanOutResultDto<object?>
        {
            Data = outcomes.FirstOrDefault(o => o.Succeeded)?.Value,
            Results = outcomes.Select(o => o.ToResultDto()).ToArray()
        };
    }

    public async Task<FanOutResultDto<object?>> PatchTtlAsync(
        string domain,
        AdminAppTtlPatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<AdminInstanceOptions> targets = ResolveTarget(request.Target);
        AdminTtlPatchRequest body = new()
        {
            OutputCacheTtlSeconds = request.OutputCacheTtlSeconds,
            FusionCacheSoftTtlSeconds = request.FusionCacheSoftTtlSeconds,
            FusionCacheHardTtlSeconds = request.FusionCacheHardTtlSeconds,
            FusionCacheFailSafeSeconds = request.FusionCacheFailSafeSeconds,
            ClientTtlSeconds = request.ClientTtlSeconds,
            ClientTtlMinSeconds = request.ClientTtlMinSeconds
        };

        List<InstanceCallOutcome<AdminDomainMutationResultDto>> outcomes =
            await FanOutAsync(
                    targets,
                    (inst, ct) => _client.PatchTtlAsync(inst, domain, body, ct),
                    cancellationToken)
                .ConfigureAwait(false);

        return new FanOutResultDto<object?>
        {
            Data = outcomes.FirstOrDefault(o => o.Succeeded)?.Value,
            Results = outcomes.Select(o => o.ToResultDto()).ToArray()
        };
    }

    private async Task<List<InstanceCallOutcome<T>>> FanOutAsync<T>(
        IReadOnlyList<AdminInstanceOptions> instances,
        Func<AdminInstanceOptions, CancellationToken, Task<InstanceCallOutcome<T>>> call,
        CancellationToken cancellationToken)
    {
        if (instances.Count == 0)
            return [];

        int parallelism = Math.Clamp(_options.Parallelism, 1, 64);
        using SemaphoreSlim gate = new(parallelism, parallelism);
        List<Task<InstanceCallOutcome<T>>> tasks = new(instances.Count);

        foreach (AdminInstanceOptions instance in instances)
        {
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
