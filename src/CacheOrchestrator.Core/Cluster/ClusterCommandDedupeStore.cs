using CacheOrchestrator.Diagnostics;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CacheOrchestrator.Cluster;

/// <summary>
/// Process-local sliding window of seen cluster <see cref="ClusterCommand.CommandId"/> values
/// to ignore duplicate delivery (best-effort, not distributed).
/// </summary>
internal sealed class ClusterCommandDedupeStore
{
    private readonly ConcurrentDictionary<Guid, long> _seen = new();
    private readonly IOptionsMonitor<ClusterCommandHandlingOptions> _options;
    private readonly TimeProvider _time;
    private long _lastCleanupTicks;

    public ClusterCommandDedupeStore(
        IOptionsMonitor<ClusterCommandHandlingOptions> options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns <see langword="true"/> if this is the first sighting within the window
    /// (caller should apply). Returns <see langword="false"/> if duplicate (skip apply).
    /// </summary>
    public bool TryMarkAsNew(Guid commandId)
    {
        if (commandId == Guid.Empty)
            return true;

        int windowSeconds = _options.CurrentValue.DedupeWindowSeconds;
        if (windowSeconds <= 0)
            return true;

        long now = _time.GetUtcNow().UtcTicks;
        long windowTicks = TimeSpan.FromSeconds(Math.Clamp(windowSeconds, 1, 3600)).Ticks;

        CleanupIfNeeded(now, windowTicks);

        if (_seen.TryGetValue(commandId, out long seenAt) && now - seenAt <= windowTicks)
        {
            CacheOrchestratorMetrics.RecordClusterDedupeHit();
            return false;
        }

        _seen[commandId] = now;
        return true;
    }

    private void CleanupIfNeeded(long now, long windowTicks)
    {
        long last = Volatile.Read(ref _lastCleanupTicks);
        // Cleanup at most once per second.
        if (now - last < TimeSpan.TicksPerSecond)
            return;
        if (Interlocked.CompareExchange(ref _lastCleanupTicks, now, last) != last)
            return;

        foreach (KeyValuePair<Guid, long> pair in _seen)
        {
            if (now - pair.Value > windowTicks)
                _seen.TryRemove(pair.Key, out _);
        }
    }
}
