namespace CacheOrchestrator.Admin;

/// <summary>
/// Process-local override snapshot for one domain (Version and/or TTL fields).
/// </summary>
public sealed class DomainRuntimeOverride
{
    /// <summary>Monotonic stamp; increments on every mutation of this domain's override.</summary>
    public int Stamp { get; init; }

    /// <summary>Runtime Version token, or null to keep configuration Version.</summary>
    public string? Version { get; init; }

    /// <summary>Override for Output Cache TTL (seconds).</summary>
    public int? OutputCacheTtlSeconds { get; init; }

    /// <summary>Override for Fusion soft TTL (seconds).</summary>
    public int? FusionCacheSoftTtlSeconds { get; init; }

    /// <summary>Override for Fusion hard TTL (seconds).</summary>
    public int? FusionCacheHardTtlSeconds { get; init; }

    /// <summary>Override for Fusion fail-safe max duration (seconds).</summary>
    public int? FusionCacheFailSafeSeconds { get; init; }

    /// <summary>Override for client max-age far from schedule (seconds).</summary>
    public int? ClientTtlSeconds { get; init; }

    /// <summary>Override for client max-age floor (seconds).</summary>
    public int? ClientTtlMinSeconds { get; init; }

    /// <summary>True when any field is set.</summary>
    public bool HasAny =>
        Version is not null
        || OutputCacheTtlSeconds is not null
        || FusionCacheSoftTtlSeconds is not null
        || FusionCacheHardTtlSeconds is not null
        || FusionCacheFailSafeSeconds is not null
        || ClientTtlSeconds is not null
        || ClientTtlMinSeconds is not null;
}

/// <summary>
/// Partial TTL update for <see cref="IDomainRuntimeOverrideStore.PatchTtl"/>.
/// Null properties mean "leave unchanged".
/// </summary>
public sealed class DomainTtlPatch
{
    /// <summary>Output Cache TTL seconds.</summary>
    public int? OutputCacheTtlSeconds { get; init; }

    /// <summary>Fusion soft TTL seconds.</summary>
    public int? FusionCacheSoftTtlSeconds { get; init; }

    /// <summary>Fusion hard TTL seconds.</summary>
    public int? FusionCacheHardTtlSeconds { get; init; }

    /// <summary>Fusion fail-safe seconds.</summary>
    public int? FusionCacheFailSafeSeconds { get; init; }

    /// <summary>Client TTL seconds (calm max-age).</summary>
    public int? ClientTtlSeconds { get; init; }

    /// <summary>Client TTL min seconds (floor).</summary>
    public int? ClientTtlMinSeconds { get; init; }

    /// <summary>True when at least one field is provided.</summary>
    public bool HasAny =>
        OutputCacheTtlSeconds is not null
        || FusionCacheSoftTtlSeconds is not null
        || FusionCacheHardTtlSeconds is not null
        || FusionCacheFailSafeSeconds is not null
        || ClientTtlSeconds is not null
        || ClientTtlMinSeconds is not null;
}
