using System.Collections.Concurrent;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.FusionCache;

/// <summary>Default in-memory <see cref="IFusionDomainRuntimeOverrideStore"/>.</summary>
internal sealed class FusionDomainRuntimeOverrideStore : IFusionDomainRuntimeOverrideStore
{
    private readonly ConcurrentDictionary<string, FusionDomainRuntimeOverride> _map =
        new(StringComparer.Ordinal);

    private int _stamp;

    /// <inheritdoc />
    public FusionDomainRuntimeOverride? Get(string domain)
    {
        string key = DomainName.Normalize(domain);
        return _map.TryGetValue(key, out FusionDomainRuntimeOverride? o) ? o : null;
    }

    /// <inheritdoc />
    public int GetStamp(string domain)
    {
        string key = DomainName.Normalize(domain);
        return _map.TryGetValue(key, out FusionDomainRuntimeOverride? o) ? o.Stamp : 0;
    }

    /// <inheritdoc />
    public FusionDomainRuntimeOverride PatchSettings(string domain, FusionDomainSettingsPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (!patch.HasAny)
            throw new ArgumentException("At least one Fusion setting must be set.", nameof(patch));

        string key = DomainName.Normalize(domain);
        return _map.AddOrUpdate(
            key,
            _ => Merge(new FusionDomainRuntimeOverride(), patch, NextStamp()),
            (_, existing) => Merge(existing, patch, NextStamp()));
    }

    /// <inheritdoc />
    public bool Clear(string domain)
    {
        string key = DomainName.Normalize(domain);
        return _map.TryRemove(key, out _);
    }

    internal static FusionDomainRuntimeOverride Merge(
        FusionDomainRuntimeOverride existing,
        FusionDomainSettingsPatch patch,
        int stamp) =>
        new()
        {
            Stamp = stamp,
            HardTtl = patch.HardTtl ?? existing.HardTtl,
            FailSafe = patch.FailSafe ?? existing.FailSafe,
            EagerRefreshRatio = patch.EagerRefreshRatio ?? existing.EagerRefreshRatio,
            Jitter = patch.Jitter ?? existing.Jitter,
            FactorySoftTimeout = patch.FactorySoftTimeout ?? existing.FactorySoftTimeout,
            FactoryHardTimeout = patch.FactoryHardTimeout ?? existing.FactoryHardTimeout,
            MaxItemBytes = patch.MaxItemBytes ?? existing.MaxItemBytes,
            AllowBackgroundDistributed = patch.AllowBackgroundDistributed ?? existing.AllowBackgroundDistributed,
            AllowBackgroundBackplane = patch.AllowBackgroundBackplane ?? existing.AllowBackgroundBackplane,
        };

    private int NextStamp() => Interlocked.Increment(ref _stamp);
}

/// <summary>No-op Fusion overlay store.</summary>
internal sealed class NullFusionDomainRuntimeOverrideStore : IFusionDomainRuntimeOverrideStore
{
    public static readonly NullFusionDomainRuntimeOverrideStore Instance = new();

    private NullFusionDomainRuntimeOverrideStore()
    {
    }

    public FusionDomainRuntimeOverride? Get(string domain) => null;

    public int GetStamp(string domain) => 0;

    public FusionDomainRuntimeOverride PatchSettings(string domain, FusionDomainSettingsPatch patch) =>
        throw new InvalidOperationException("Fusion runtime overlays are not available.");

    public bool Clear(string domain) => false;
}
