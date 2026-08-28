using CacheOrchestrator.Configuration;
using System.Collections.Concurrent;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Default in-memory <see cref="IDomainRuntimeOverrideStore"/>.
/// </summary>
internal sealed class DomainRuntimeOverrideStore : IDomainRuntimeOverrideStore
{
    private readonly ConcurrentDictionary<string, DomainRuntimeOverride> _map =
        new(StringComparer.Ordinal);

    private int _stamp;

    /// <inheritdoc />
    public DomainRuntimeOverride? Get(string domain)
    {
        string key = DomainName.Normalize(domain);
        return _map.TryGetValue(key, out DomainRuntimeOverride? o) ? o : null;
    }

    /// <inheritdoc />
    public int GetStamp(string domain)
    {
        string key = DomainName.Normalize(domain);
        return _map.TryGetValue(key, out DomainRuntimeOverride? o) ? o.Stamp : 0;
    }

    /// <inheritdoc />
    public DomainRuntimeOverride SetVersion(string domain, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        string key = DomainName.Normalize(domain);
        string v = version.Trim();

        return _map.AddOrUpdate(
            key,
            _ => new DomainRuntimeOverride { Stamp = NextStamp(), Version = v },
            (_, existing) => WithVersion(existing, v, NextStamp()));
    }

    /// <inheritdoc />
    public DomainRuntimeOverride PatchSettings(string domain, DomainSettingsPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (!patch.HasAny)
            throw new ArgumentException("At least one setting must be set.", nameof(patch));

        string key = DomainName.Normalize(domain);

        return _map.AddOrUpdate(
            key,
            _ => FromPatch(patch, version: null, NextStamp()),
            (_, existing) => Merge(existing, patch, NextStamp()));
    }

    /// <inheritdoc />
    public bool Clear(string domain)
    {
        string key = DomainName.Normalize(domain);
        return _map.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetOverriddenDomains() => [.. _map.Keys];

    internal static DomainRuntimeOverride FromPatch(DomainSettingsPatch patch, string? version, int stamp) =>
        Merge(new DomainRuntimeOverride { Stamp = stamp, Version = version }, patch, stamp);

    internal static DomainRuntimeOverride Merge(DomainRuntimeOverride existing, DomainSettingsPatch patch, int stamp) =>
        new()
        {
            Stamp = stamp,
            Version = existing.Version,
            DataCacheEnabled = patch.DataCacheEnabled ?? existing.DataCacheEnabled,
            DataCacheTtl = patch.DataCacheTtl ?? existing.DataCacheTtl,
        };

    private static DomainRuntimeOverride WithVersion(DomainRuntimeOverride existing, string version, int stamp) =>
        new()
        {
            Stamp = stamp,
            Version = version,
            DataCacheEnabled = existing.DataCacheEnabled,
            DataCacheTtl = existing.DataCacheTtl,
        };

    private int NextStamp() => Interlocked.Increment(ref _stamp);
}

/// <summary>No-op store used when Admin is disabled.</summary>
internal sealed class NullDomainRuntimeOverrideStore : IDomainRuntimeOverrideStore
{
    public static readonly NullDomainRuntimeOverrideStore Instance = new();

    private NullDomainRuntimeOverrideStore()
    {
    }

    public DomainRuntimeOverride? Get(string domain) => null;

    public int GetStamp(string domain) => 0;

    public DomainRuntimeOverride SetVersion(string domain, string version) =>
        throw new InvalidOperationException("Admin runtime overrides are disabled.");

    public DomainRuntimeOverride PatchSettings(string domain, DomainSettingsPatch patch) =>
        throw new InvalidOperationException("Admin runtime overrides are disabled.");

    public bool Clear(string domain) => false;

    public IReadOnlyCollection<string> GetOverriddenDomains() => [];
}
