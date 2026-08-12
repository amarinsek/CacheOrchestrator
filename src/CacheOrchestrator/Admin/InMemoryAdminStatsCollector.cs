using CacheOrchestrator.Configuration;
using System.Collections.Concurrent;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Process-local live counters for Local Admin API.
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
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(adminOptions);
        TrackEndpoints = adminOptions.TrackEndpoints;
        TrackLatency = adminOptions.TrackLatency;
        _instanceId = AdminInstanceId.Resolve(adminOptions);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool TrackEndpoints { get; }

    /// <inheritdoc />
    public bool TrackLatency { get; }

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
    public void RecordFusion(string? endpointKey, string? domain, string result, long? elapsedTicks = null)
    {
        if (!string.IsNullOrEmpty(domain))
            ApplyFusion(GetDomain(domain), result, elapsedTicks);

        if (TrackEndpoints && !string.IsNullOrEmpty(endpointKey))
        {
            AdminCounterSet ep = GetEndpoint(endpointKey);
            ApplyFusion(ep, result, elapsedTicks);
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
    public AdminLiveStatsSnapshot GetSnapshot()
    {
        // Raw counter snapshot only; domain enrichment happens in AdminQueryService.
        List<AdminDomainStatsDto> domains = [];
        foreach ((string name, AdminCounterSet counters) in _domains)
        {
            long invTicks = Interlocked.Read(ref counters.LastInvalidationUtcTicks);
            (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) =
                counters.ToStats();
            domains.Add(new AdminDomainStatsDto
            {
                Name = name,
                InstanceId = _instanceId,
                Version = string.Empty,
                Requests = requests,
                Oc = oc,
                Fc = fc,
                Pipeline = pipeline,
                Invalidations = Interlocked.Read(ref counters.Invalidations),
                LastInvalidationUtc = invTicks > 0
                    ? new DateTimeOffset(invTicks, TimeSpan.Zero)
                    : null,
                Endpoints = []
            });
        }

        List<AdminEndpointStatsDto> endpoints = [];
        foreach ((string route, AdminCounterSet counters) in _endpoints)
        {
            _endpointConfiguredDomain.TryGetValue(route, out string? configured);
            (long requests, AdminLayerDto oc, AdminFusionLayerDto fc, AdminPipelineDto pipeline) =
                counters.ToStats();
            endpoints.Add(new AdminEndpointStatsDto
            {
                Route = route,
                InstanceId = _instanceId,
                ConfiguredDomain = configured,
                Requests = requests,
                Oc = oc,
                Fc = fc,
                Pipeline = pipeline
            });
        }

        return new AdminLiveStatsSnapshot
        {
            InstanceId = _instanceId,
            CollectedAtUtc = _time.GetUtcNow(),
            Domains = [.. domains.OrderBy(d => d.Name, StringComparer.Ordinal)],
            UnassignedEndpoints = endpoints,
            Endpoints = endpoints
        };
    }

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

    private void ApplyOutput(AdminCounterSet set, string result)
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

    private void ApplyFusion(AdminCounterSet set, string result, long? elapsedTicks)
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

        if (TrackLatency && elapsedTicks is long ticks)
        {
            Interlocked.Add(ref set.FactorySumTicks, ticks);
            Interlocked.Increment(ref set.FactoryCount);
        }
    }
}
