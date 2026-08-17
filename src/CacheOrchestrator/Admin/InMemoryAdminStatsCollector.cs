using CacheOrchestrator.Configuration;
using System.Collections.Concurrent;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Process-local live counters for Local Admin API.
/// Stores raw counters only; fat v1 DTOs are projected on read.
/// </summary>
internal sealed class InMemoryAdminStatsCollector : IAdminStatsCollector
{
    private readonly ConcurrentDictionary<string, AdminCounterSet> _domains =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, AdminCounterSet> _endpoints =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string?> _endpointConfiguredDomain =
        new(StringComparer.Ordinal);

    private readonly string _instanceId;
    private readonly TimeProvider _time;

    public InMemoryAdminStatsCollector(
        CacheOrchestratorOptions.AdminOptions adminOptions,
        string instanceId,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(adminOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        TrackEndpoints = adminOptions.TrackEndpoints;
        TrackLatency = adminOptions.TrackLatency;
        TrackResultSize = adminOptions.TrackResultSize;
        _instanceId = instanceId.Trim();
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool TrackEndpoints { get; }

    /// <inheritdoc />
    public bool TrackLatency { get; }

    /// <inheritdoc />
    public bool TrackResultSize { get; }

    /// <inheritdoc />
    public void RecordOutput(string? endpointKey, string? domain, string result)
    {
        if (!string.IsNullOrEmpty(domain))
            ApplyOutput(GetDomain(domain), result);

        if (TrackEndpoints && !string.IsNullOrEmpty(endpointKey))
        {
            AdminCounterSet ep = GetEndpoint(endpointKey);
            ApplyOutput(ep, result);
            RememberEndpointDomain(endpointKey, domain);
        }
    }

    /// <inheritdoc />
    public void RecordFusion(
        string? endpointKey,
        string? domain,
        string result,
        long? elapsedTicks = null,
        long? resultSizeBytes = null)
    {
        if (!string.IsNullOrEmpty(domain))
            ApplyFusion(GetDomain(domain), result, elapsedTicks, resultSizeBytes);

        if (TrackEndpoints && !string.IsNullOrEmpty(endpointKey))
        {
            AdminCounterSet ep = GetEndpoint(endpointKey);
            ApplyFusion(ep, result, elapsedTicks, resultSizeBytes);
            RememberEndpointDomain(endpointKey, domain);
        }
    }

    /// <inheritdoc />
    public void RecordInvalidation(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return;

        AdminCounterSet set = GetDomain(DomainName.Normalize(domain));
        Interlocked.Increment(ref set.Invalidations);
        Interlocked.Exchange(ref set.LastInvalidationUtcTicks, _time.GetUtcNow().UtcTicks);
    }

    /// <inheritdoc />
    public AdminLiveStatsRawSnapshot GetRawSnapshot()
    {
        List<AdminDomainCountersDto> domains = [];
        foreach ((string name, AdminCounterSet counters) in _domains)
        {
            AdminCounterSnapshot c = counters.Read();
            domains.Add(ToDomainCounters(name, _instanceId, c));
        }

        List<AdminEndpointCountersDto> endpoints = [];
        foreach ((string route, AdminCounterSet counters) in _endpoints)
        {
            _endpointConfiguredDomain.TryGetValue(route, out string? configured);
            AdminCounterSnapshot c = counters.Read();
            endpoints.Add(ToEndpointCounters(route, _instanceId, configured, c));
        }

        return new AdminLiveStatsRawSnapshot
        {
            InstanceId = _instanceId,
            CollectedAtUtc = _time.GetUtcNow(),
            Domains = [.. domains.OrderBy(d => d.Name, StringComparer.Ordinal)],
            UnassignedEndpoints = endpoints,
            Endpoints = endpoints
        };
    }

    /// <inheritdoc />
    public AdminLiveStatsSnapshot GetSnapshot() =>
        AdminStatsV1Mapper.ToLiveSnapshot(GetRawSnapshot());

    /// <summary>Exposes endpoint→domain hints recorded at runtime (for snapshot assembly).</summary>
    internal IReadOnlyDictionary<string, string?> EndpointDomainHints => _endpointConfiguredDomain;

    private AdminCounterSet GetDomain(string domain) =>
        _domains.GetOrAdd(DomainName.Normalize(domain), static _ => new AdminCounterSet());

    private AdminCounterSet GetEndpoint(string endpointKey) =>
        _endpoints.GetOrAdd(endpointKey, static _ => new AdminCounterSet());

    private void RememberEndpointDomain(string endpointKey, string? domain)
    {
        if (string.IsNullOrEmpty(domain))
            return;

        string normalized = DomainName.Normalize(domain);
        _endpointConfiguredDomain.AddOrUpdate(
            endpointKey,
            normalized,
            (_, existing) => existing ?? normalized);
    }

    private static void ApplyOutput(AdminCounterSet set, string result)
    {
        switch (result)
        {
            case "hit":
                Interlocked.Increment(ref set.OcHits);
                break;
            case "miss":
                Interlocked.Increment(ref set.OcMisses);
                break;
            case "bypass":
                Interlocked.Increment(ref set.OcBypass);
                break;
            default:
                break;
        }
    }

    private void ApplyFusion(
        AdminCounterSet set,
        string result,
        long? elapsedTicks,
        long? resultSizeBytes)
    {
        switch (result)
        {
            case "hit":
                Interlocked.Increment(ref set.FcHits);
                break;
            case "miss":
                Interlocked.Increment(ref set.FcMisses);
                Interlocked.Increment(ref set.FcFactoryRuns);
                break;
            case "stale":
                Interlocked.Increment(ref set.FcStale);
                Interlocked.Increment(ref set.FcFactoryFailures);
                break;
            case "bypass":
                Interlocked.Increment(ref set.FcBypass);
                break;
            default:
                // off / unresolved etc. — not counted as FC hit/miss traffic
                break;
        }

        // Factory-path duration only (miss / stale). Hits must not dilute avg factory cost.
        if (TrackLatency
            && elapsedTicks is long ticks
            && IsFactoryPathResult(result))
        {
            Interlocked.Add(ref set.FactorySumTicks, ticks);
            Interlocked.Increment(ref set.FactoryCount);
        }

        // Successful factory materialization size only (miss with known size).
        if (TrackResultSize
            && resultSizeBytes is long size
            && size >= 0
            && result is "miss")
        {
            Interlocked.Add(ref set.FactoryResultSizeSumBytes, size);
            Interlocked.Increment(ref set.FactoryResultSizeCount);
        }
    }

    /// <summary>Results where the value factory ran (success or fail-safe failure path).</summary>
    internal static bool IsFactoryPathResult(string result) =>
        result is "miss" or "stale";

    private static AdminDomainCountersDto ToDomainCounters(
        string name,
        string instanceId,
        in AdminCounterSnapshot c)
    {
        DateTimeOffset? lastInv = c.LastInvalidationUtcTicks > 0
            ? new DateTimeOffset(c.LastInvalidationUtcTicks, TimeSpan.Zero)
            : null;

        return new AdminDomainCountersDto
        {
            Name = name,
            InstanceId = instanceId,
            Version = string.Empty,
            LastInvalidationUtc = lastInv,
            Invalidations = c.Invalidations,
            OcHits = c.OcHits,
            OcMisses = c.OcMisses,
            OcBypass = c.OcBypass,
            FcHits = c.FcHits,
            FcMisses = c.FcMisses,
            FcStale = c.FcStale,
            FcBypass = c.FcBypass,
            FactoryRuns = c.FactoryRuns,
            FactoryFailures = c.FactoryFailures,
            FactoryDurationSumMs = c.FactoryDurationSumMs,
            FactoryDurationCount = c.FactoryDurationCount,
            FactoryResultSizeSumBytes = c.FactoryResultSizeSumBytes,
            FactoryResultSizeCount = c.FactoryResultSizeCount,
            Endpoints = []
        };
    }

    private static AdminEndpointCountersDto ToEndpointCounters(
        string route,
        string instanceId,
        string? configuredDomain,
        in AdminCounterSnapshot c) =>
        new()
        {
            Route = route,
            InstanceId = instanceId,
            ConfiguredDomain = configuredDomain,
            OcHits = c.OcHits,
            OcMisses = c.OcMisses,
            OcBypass = c.OcBypass,
            FcHits = c.FcHits,
            FcMisses = c.FcMisses,
            FcStale = c.FcStale,
            FcBypass = c.FcBypass,
            FactoryRuns = c.FactoryRuns,
            FactoryFailures = c.FactoryFailures,
            FactoryDurationSumMs = c.FactoryDurationSumMs,
            FactoryDurationCount = c.FactoryDurationCount,
            FactoryResultSizeSumBytes = c.FactoryResultSizeSumBytes,
            FactoryResultSizeCount = c.FactoryResultSizeCount
        };
}
