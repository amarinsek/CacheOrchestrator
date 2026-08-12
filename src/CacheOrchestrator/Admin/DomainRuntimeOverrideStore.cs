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
            _ => new DomainRuntimeOverride
            {
                Stamp = NextStamp(),
                Version = v
            },
            (_, existing) => new DomainRuntimeOverride
            {
                Stamp = NextStamp(),
                Version = v,
                OutputCacheTtlSeconds = existing.OutputCacheTtlSeconds,
                FusionCacheSoftTtlSeconds = existing.FusionCacheSoftTtlSeconds,
                FusionCacheHardTtlSeconds = existing.FusionCacheHardTtlSeconds,
                FusionCacheFailSafeSeconds = existing.FusionCacheFailSafeSeconds,
                ClientTtlSeconds = existing.ClientTtlSeconds,
                ClientTtlMinSeconds = existing.ClientTtlMinSeconds
            });
    }

    /// <inheritdoc />
    public DomainRuntimeOverride PatchTtl(string domain, DomainTtlPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (!patch.HasAny)
            throw new ArgumentException("At least one TTL field must be set.", nameof(patch));

        string key = DomainName.Normalize(domain);

        return _map.AddOrUpdate(
            key,
            _ => FromPatch(patch, version: null),
            (_, existing) => new DomainRuntimeOverride
            {
                Stamp = NextStamp(),
                Version = existing.Version,
                OutputCacheTtlSeconds = patch.OutputCacheTtlSeconds ?? existing.OutputCacheTtlSeconds,
                FusionCacheSoftTtlSeconds = patch.FusionCacheSoftTtlSeconds ?? existing.FusionCacheSoftTtlSeconds,
                FusionCacheHardTtlSeconds = patch.FusionCacheHardTtlSeconds ?? existing.FusionCacheHardTtlSeconds,
                FusionCacheFailSafeSeconds = patch.FusionCacheFailSafeSeconds ?? existing.FusionCacheFailSafeSeconds,
                ClientTtlSeconds = patch.ClientTtlSeconds ?? existing.ClientTtlSeconds,
                ClientTtlMinSeconds = patch.ClientTtlMinSeconds ?? existing.ClientTtlMinSeconds
            });
    }

    /// <inheritdoc />
    public bool Clear(string domain)
    {
        string key = DomainName.Normalize(domain);
        return _map.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetOverriddenDomains() => [.. _map.Keys];

    private DomainRuntimeOverride FromPatch(DomainTtlPatch patch, string? version) =>
        new()
        {
            Stamp = NextStamp(),
            Version = version,
            OutputCacheTtlSeconds = patch.OutputCacheTtlSeconds,
            FusionCacheSoftTtlSeconds = patch.FusionCacheSoftTtlSeconds,
            FusionCacheHardTtlSeconds = patch.FusionCacheHardTtlSeconds,
            FusionCacheFailSafeSeconds = patch.FusionCacheFailSafeSeconds,
            ClientTtlSeconds = patch.ClientTtlSeconds,
            ClientTtlMinSeconds = patch.ClientTtlMinSeconds
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

    public DomainRuntimeOverride PatchTtl(string domain, DomainTtlPatch patch) =>
        throw new InvalidOperationException("Admin runtime overrides are disabled.");

    public bool Clear(string domain) => false;

    public IReadOnlyCollection<string> GetOverriddenDomains() => [];
}
