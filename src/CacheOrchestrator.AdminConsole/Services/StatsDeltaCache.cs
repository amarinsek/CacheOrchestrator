using System.Collections.Concurrent;
using CacheOrchestrator.Admin;

namespace CacheOrchestrator.AdminConsole.Services;

/// <summary>
/// In-memory poll samples so Overview can show impact over the last Console poll interval
/// (approximate “now” window without Prometheus).
/// </summary>
public sealed class StatsDeltaCache
{
    private readonly ConcurrentDictionary<string, Sample> _last = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public StatsDeltaCache(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Records a cluster lifetime sample and returns impact for the delta since the previous sample
    /// (same <paramref name="scopeKey"/>), when the previous sample exists and counters did not reset.
    /// </summary>
    public (CacheImpactKpiDto? Impact, string? WindowLabel) RecordAndDiff(
        string scopeKey,
        long requests,
        long factoryRuns,
        double? factoryDurationSumMs,
        long factoryDurationCount,
        long? factoryResultSizeSumBytes = null,
        long factoryResultSizeCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);

        DateTimeOffset now = _time.GetUtcNow();
        Sample current = new(
            now,
            requests,
            factoryRuns,
            factoryDurationSumMs ?? 0,
            factoryDurationCount,
            factoryResultSizeSumBytes ?? 0,
            factoryResultSizeCount);

        CacheImpactKpiDto? deltaImpact = null;
        string? windowLabel = null;

        if (_last.TryGetValue(scopeKey, out Sample? prev))
        {
            long dReq = current.Requests - prev.Requests;
            long dRuns = current.FactoryRuns - prev.FactoryRuns;
            double dSum = current.DurationSumMs - prev.DurationSumMs;
            long dCount = current.DurationCount - prev.DurationCount;
            long dSize = current.SizeSumBytes - prev.SizeSumBytes;
            long dSizeCount = current.SizeCount - prev.SizeCount;

            // Counter reset (process restart) or clock weirdness → skip delta.
            if (dReq >= 0 && dRuns >= 0 && dCount >= 0 && dSum >= -0.001
                && dSizeCount >= 0 && dSize >= 0)
            {
                TimeSpan elapsed = current.At - prev.At;
                if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(6))
                {
                    deltaImpact = ImpactMath.Compute(
                        dReq,
                        dRuns,
                        dCount > 0 ? dSum : null,
                        dCount,
                        dSizeCount > 0 ? dSize : null,
                        dSizeCount);
                    windowLabel = elapsed.TotalSeconds < 90
                        ? $"last ~{Math.Max(1, (int)elapsed.TotalSeconds)}s (poll delta)"
                        : $"last ~{elapsed.TotalMinutes:0.#}m (poll delta)";
                }
            }
        }

        _last[scopeKey] = current;
        return (deltaImpact, windowLabel);
    }

    private sealed record Sample(
        DateTimeOffset At,
        long Requests,
        long FactoryRuns,
        double DurationSumMs,
        long DurationCount,
        long SizeSumBytes,
        long SizeCount);
}
