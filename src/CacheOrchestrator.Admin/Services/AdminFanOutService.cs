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
                LatencyMs = o.LatencyMs
            };
        }).ToArray();
    }

    public async Task<ClusterStatsDto> GetStatsAsync(
        string? scope,
        CancellationToken cancellationToken,
        bool groupByInstance = false)
    {
        IReadOnlyList<AdminInstanceOptions> targets = ResolveTarget(NormalizeScopeToTarget(scope));
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

        return new ClusterStatsDto
        {
            Scope = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim(),
            GroupByInstance = groupByInstance,
            CollectedAtUtc = _time.GetUtcNow(),
            Instances = contributions,
            Domains = StatsAggregator.MergeDomains(ok, groupByInstance),
            Endpoints = StatsAggregator.MergeEndpoints(ok, groupByInstance),
            UnassignedEndpoints = StatsAggregator.MergeUnassignedEndpoints(ok, groupByInstance)
        };
    }

    public async Task<IReadOnlyList<AdminEndpointStatsDto>> GetTopEndpointsAsync(
        string? sort,
        int take,
        CancellationToken cancellationToken,
        bool groupByInstance = false)
    {
        ClusterStatsDto stats = await GetStatsAsync("all", cancellationToken, groupByInstance)
            .ConfigureAwait(false);
        IEnumerable<AdminEndpointStatsDto> all = stats.Endpoints;

        take = Math.Clamp(take, 1, 500);
        string sortKey = (sort ?? "originShare").Trim().ToLowerInvariant();

        IOrderedEnumerable<AdminEndpointStatsDto> ordered = sortKey switch
        {
            "hits" or "traffic" or "requests" => all.OrderByDescending(e => e.Requests),
            "ochitshare" => all.OrderByDescending(e => e.Oc.HitShare ?? -1),
            "ocmissrate" => all.OrderByDescending(e => e.Oc.MissRate ?? -1),
            "fchitshare" => all.OrderByDescending(e => e.Fc.HitShare ?? -1),
            "fcmissshare" => all.OrderByDescending(e => e.Fc.MissShare ?? -1),
            "fcmissrate" or "missrate" => all.OrderByDescending(e => e.Fc.MissRate ?? -1),
            "fchits" => all.OrderByDescending(e => e.Fc.Hits),
            _ => all.OrderByDescending(e => e.Fc.OriginShare ?? e.Fc.MissShare ?? -1)
        };

        return ordered.Take(take).ToArray();
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

}
