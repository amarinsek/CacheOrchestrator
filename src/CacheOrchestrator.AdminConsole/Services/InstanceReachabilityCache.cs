using System.Collections.Concurrent;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AdminConsole.Services;

/// <summary>
/// Process-local cache of instance health so fan-out skips known-down targets
/// until a re-probe interval elapses (avoids stacking request timeouts).
/// </summary>
public sealed class InstanceReachabilityCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly AdminConsoleOptions _options;
    private readonly TimeProvider _time;

    public InstanceReachabilityCache(IOptions<AdminConsoleOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _options = options.Value;
        _time = time;
    }

    /// <summary>Re-probe interval for instances marked Down.</summary>
    public TimeSpan DownReprobeInterval =>
        TimeSpan.FromSeconds(Math.Clamp(_options.DownReprobeSeconds, 5, 300));

    /// <summary>
    /// True when the instance is known Down and the re-probe window has not elapsed.
    /// Callers should skip HTTP and treat the instance as down immediately.
    /// </summary>
    public bool ShouldSkipUnreachable(string instanceId)
    {
        if (!_entries.TryGetValue(instanceId, out Entry? e))
            return false;
        if (e.Status != InstanceHealthStatus.Down)
            return false;
        return _time.GetUtcNow() < e.NextProbeUtc;
    }

    /// <summary>Cached snapshot when skip applies; otherwise null.</summary>
    public CachedInstanceHealth? TryGetSkippedDown(string instanceId)
    {
        if (!ShouldSkipUnreachable(instanceId))
            return null;
        if (!_entries.TryGetValue(instanceId, out Entry? e))
            return null;
        return new CachedInstanceHealth(e.Status, e.Error, e.LatencyMs, e.CheckedAtUtc, e.ReportedInstanceId);
    }

    /// <summary>Records a successful Local Admin call (any endpoint).</summary>
    public void RecordSuccess(string instanceId, string? reportedInstanceId = null, double? latencyMs = null)
    {
        DateTimeOffset now = _time.GetUtcNow();
        _entries.AddOrUpdate(
            instanceId,
            _ => new Entry
            {
                Status = InstanceHealthStatus.Healthy,
                CheckedAtUtc = now,
                NextProbeUtc = now,
                ReportedInstanceId = reportedInstanceId,
                LatencyMs = latencyMs,
                Error = null
            },
            (_, existing) => existing with
            {
                Status = InstanceHealthStatus.Healthy,
                CheckedAtUtc = now,
                NextProbeUtc = now,
                ReportedInstanceId = reportedInstanceId ?? existing.ReportedInstanceId,
                LatencyMs = latencyMs,
                Error = null
            });
    }

    /// <summary>Records a failed call; instance is skipped until <see cref="DownReprobeInterval"/>.</summary>
    public void RecordFailure(string instanceId, string? error, double? latencyMs = null)
    {
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset next = now + DownReprobeInterval;
        _entries.AddOrUpdate(
            instanceId,
            _ => new Entry
            {
                Status = InstanceHealthStatus.Down,
                CheckedAtUtc = now,
                NextProbeUtc = next,
                Error = error,
                LatencyMs = latencyMs
            },
            (_, existing) => existing with
            {
                Status = InstanceHealthStatus.Down,
                CheckedAtUtc = now,
                NextProbeUtc = next,
                Error = error ?? existing.Error,
                LatencyMs = latencyMs
            });
    }

    /// <summary>Records an explicit health probe result.</summary>
    public void RecordHealth(
        string instanceId,
        InstanceHealthStatus status,
        string? error,
        double? latencyMs,
        string? reportedInstanceId)
    {
        if (status == InstanceHealthStatus.Down)
        {
            RecordFailure(instanceId, error, latencyMs);
            if (_entries.TryGetValue(instanceId, out Entry? e) && reportedInstanceId is not null)
            {
                _entries[instanceId] = e with { ReportedInstanceId = reportedInstanceId };
            }

            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        _entries.AddOrUpdate(
            instanceId,
            _ => new Entry
            {
                Status = status,
                CheckedAtUtc = now,
                NextProbeUtc = now,
                Error = error,
                LatencyMs = latencyMs,
                ReportedInstanceId = reportedInstanceId
            },
            (_, existing) => existing with
            {
                Status = status,
                CheckedAtUtc = now,
                NextProbeUtc = now,
                Error = error,
                LatencyMs = latencyMs,
                ReportedInstanceId = reportedInstanceId ?? existing.ReportedInstanceId
            });
    }

    private sealed record Entry
    {
        public required InstanceHealthStatus Status { get; init; }
        public required DateTimeOffset CheckedAtUtc { get; init; }
        public required DateTimeOffset NextProbeUtc { get; init; }
        public string? Error { get; init; }
        public double? LatencyMs { get; init; }
        public string? ReportedInstanceId { get; init; }
    }
}

/// <summary>Cached health used when skipping a known-down instance.</summary>
public sealed record CachedInstanceHealth(
    InstanceHealthStatus Status,
    string? Error,
    double? LatencyMs,
    DateTimeOffset CheckedAtUtc,
    string? ReportedInstanceId);
