namespace CacheOrchestrator.Admin;

/// <summary>
/// Process-local override snapshot for one domain (Version and/or settings fields).
/// Null property = inherit from configuration.
/// </summary>
public sealed class DomainRuntimeOverride
{
    /// <summary>Monotonic stamp; increments on every mutation of this domain's override.</summary>
    public int Stamp { get; init; }

    /// <summary>Runtime Version token, or null to keep configuration Version.</summary>
    public string? Version { get; init; }

    // —— Overlay scalars (aligned with DomainSettingAttribute RuntimeOverlay = true) ——

    /// <summary>Override Output Cache enabled.</summary>
    public bool? OutputCacheEnabled { get; init; }

    /// <summary>Override FusionCache enabled.</summary>
    public bool? FusionCacheEnabled { get; init; }

    /// <summary>Override bypass-when-authenticated.</summary>
    public bool? BypassWhenAuthenticated { get; init; }

    /// <summary>Override vary-by-user.</summary>
    public bool? VaryOutputCacheByUser { get; init; }

    /// <summary>Override ETag mode.</summary>
    public Configuration.ETagMode? ETagMode { get; init; }

    /// <summary>Override client cacheability.</summary>
    public Configuration.ClientCacheability? ClientCacheability { get; init; }

    /// <summary>Override client TTL seconds.</summary>
    public int? ClientTtlSeconds { get; init; }

    /// <summary>Override client TTL min seconds.</summary>
    public int? ClientTtlMinSeconds { get; init; }

    /// <summary>Override scheduled update UTC.</summary>
    public DateTimeOffset? ScheduledUpdateUtc { get; init; }

    /// <summary>Override must-revalidate near update.</summary>
    public bool? ClientMustRevalidateNearUpdate { get; init; }

    /// <summary>Override Output Cache TTL seconds.</summary>
    public int? OutputCacheTtlSeconds { get; init; }

    /// <summary>Override Fusion soft TTL seconds.</summary>
    public int? FusionCacheSoftTtlSeconds { get; init; }

    /// <summary>Override Fusion hard TTL seconds.</summary>
    public int? FusionCacheHardTtlSeconds { get; init; }

    /// <summary>Override Fusion fail-safe seconds.</summary>
    public int? FusionCacheFailSafeSeconds { get; init; }

    /// <summary>Override eager refresh ratio.</summary>
    public double? FusionCacheEagerRefreshRatio { get; init; }

    /// <summary>Override Fusion jitter seconds.</summary>
    public int? FusionCacheJitterSeconds { get; init; }

    /// <summary>Override factory soft timeout seconds.</summary>
    public int? FusionCacheFactorySoftTimeoutSeconds { get; init; }

    /// <summary>Override factory hard timeout seconds.</summary>
    public int? FusionCacheFactoryHardTimeoutSeconds { get; init; }

    /// <summary>Override max item bytes.</summary>
    public int? FusionCacheMaxItemBytes { get; init; }

    /// <summary>Override respect no-store.</summary>
    public bool? FusionCacheRespectNoStore { get; init; }

    /// <summary>Override background distributed ops.</summary>
    public bool? FusionCacheAllowBackgroundDistributed { get; init; }

    /// <summary>Override background backplane ops.</summary>
    public bool? FusionCacheAllowBackgroundBackplane { get; init; }

    /// <summary>Override vary on public address.</summary>
    public bool? FusionCacheVaryOnPublicAddress { get; init; }

    /// <summary>Override vary on encoding.</summary>
    public bool? FusionCacheVaryOnEncoding { get; init; }

    /// <summary>Override Output Cache vary-by-host.</summary>
    public bool? OutputCacheVaryByHost { get; init; }

    /// <summary>True when any field is set.</summary>
    public bool HasAny =>
        Version is not null
        || OutputCacheEnabled is not null
        || FusionCacheEnabled is not null
        || BypassWhenAuthenticated is not null
        || VaryOutputCacheByUser is not null
        || ETagMode is not null
        || ClientCacheability is not null
        || ClientTtlSeconds is not null
        || ClientTtlMinSeconds is not null
        || ScheduledUpdateUtc is not null
        || ClientMustRevalidateNearUpdate is not null
        || OutputCacheTtlSeconds is not null
        || FusionCacheSoftTtlSeconds is not null
        || FusionCacheHardTtlSeconds is not null
        || FusionCacheFailSafeSeconds is not null
        || FusionCacheEagerRefreshRatio is not null
        || FusionCacheJitterSeconds is not null
        || FusionCacheFactorySoftTimeoutSeconds is not null
        || FusionCacheFactoryHardTimeoutSeconds is not null
        || FusionCacheMaxItemBytes is not null
        || FusionCacheRespectNoStore is not null
        || FusionCacheAllowBackgroundDistributed is not null
        || FusionCacheAllowBackgroundBackplane is not null
        || FusionCacheVaryOnPublicAddress is not null
        || FusionCacheVaryOnEncoding is not null
        || OutputCacheVaryByHost is not null;
}

/// <summary>
/// Partial settings update for <see cref="IDomainRuntimeOverrideStore.PatchSettings"/>.
/// Null properties mean "leave unchanged".
/// </summary>
public sealed class DomainSettingsPatch
{
    /// <summary>Output Cache enabled.</summary>
    public bool? OutputCacheEnabled { get; init; }

    /// <summary>FusionCache enabled.</summary>
    public bool? FusionCacheEnabled { get; init; }

    /// <summary>Bypass when authenticated.</summary>
    public bool? BypassWhenAuthenticated { get; init; }

    /// <summary>Vary Output Cache by user.</summary>
    public bool? VaryOutputCacheByUser { get; init; }

    /// <summary>ETag mode.</summary>
    public Configuration.ETagMode? ETagMode { get; init; }

    /// <summary>Client cacheability.</summary>
    public Configuration.ClientCacheability? ClientCacheability { get; init; }

    /// <summary>Client TTL seconds.</summary>
    public int? ClientTtlSeconds { get; init; }

    /// <summary>Client TTL min seconds.</summary>
    public int? ClientTtlMinSeconds { get; init; }

    /// <summary>Scheduled update UTC.</summary>
    public DateTimeOffset? ScheduledUpdateUtc { get; init; }

    /// <summary>Must-revalidate near update.</summary>
    public bool? ClientMustRevalidateNearUpdate { get; init; }

    /// <summary>Output Cache TTL seconds.</summary>
    public int? OutputCacheTtlSeconds { get; init; }

    /// <summary>Fusion soft TTL seconds.</summary>
    public int? FusionCacheSoftTtlSeconds { get; init; }

    /// <summary>Fusion hard TTL seconds.</summary>
    public int? FusionCacheHardTtlSeconds { get; init; }

    /// <summary>Fusion fail-safe seconds.</summary>
    public int? FusionCacheFailSafeSeconds { get; init; }

    /// <summary>Eager refresh ratio.</summary>
    public double? FusionCacheEagerRefreshRatio { get; init; }

    /// <summary>Fusion jitter seconds.</summary>
    public int? FusionCacheJitterSeconds { get; init; }

    /// <summary>Factory soft timeout seconds.</summary>
    public int? FusionCacheFactorySoftTimeoutSeconds { get; init; }

    /// <summary>Factory hard timeout seconds.</summary>
    public int? FusionCacheFactoryHardTimeoutSeconds { get; init; }

    /// <summary>Max item bytes.</summary>
    public int? FusionCacheMaxItemBytes { get; init; }

    /// <summary>Respect no-store.</summary>
    public bool? FusionCacheRespectNoStore { get; init; }

    /// <summary>Background distributed ops.</summary>
    public bool? FusionCacheAllowBackgroundDistributed { get; init; }

    /// <summary>Background backplane ops.</summary>
    public bool? FusionCacheAllowBackgroundBackplane { get; init; }

    /// <summary>Vary on public address.</summary>
    public bool? FusionCacheVaryOnPublicAddress { get; init; }

    /// <summary>Vary on encoding.</summary>
    public bool? FusionCacheVaryOnEncoding { get; init; }

    /// <summary>Vary Output Cache by host.</summary>
    public bool? OutputCacheVaryByHost { get; init; }

    /// <summary>True when at least one field is provided.</summary>
    public bool HasAny =>
        OutputCacheEnabled is not null
        || FusionCacheEnabled is not null
        || BypassWhenAuthenticated is not null
        || VaryOutputCacheByUser is not null
        || ETagMode is not null
        || ClientCacheability is not null
        || ClientTtlSeconds is not null
        || ClientTtlMinSeconds is not null
        || ScheduledUpdateUtc is not null
        || ClientMustRevalidateNearUpdate is not null
        || OutputCacheTtlSeconds is not null
        || FusionCacheSoftTtlSeconds is not null
        || FusionCacheHardTtlSeconds is not null
        || FusionCacheFailSafeSeconds is not null
        || FusionCacheEagerRefreshRatio is not null
        || FusionCacheJitterSeconds is not null
        || FusionCacheFactorySoftTimeoutSeconds is not null
        || FusionCacheFactoryHardTimeoutSeconds is not null
        || FusionCacheMaxItemBytes is not null
        || FusionCacheRespectNoStore is not null
        || FusionCacheAllowBackgroundDistributed is not null
        || FusionCacheAllowBackgroundBackplane is not null
        || FusionCacheVaryOnPublicAddress is not null
        || FusionCacheVaryOnEncoding is not null
        || OutputCacheVaryByHost is not null;

    /// <summary>True when this patch only touches the legacy TTL six-pack (safe for old <c>ttlPatch</c> peers).</summary>
    public bool IsTtlOnly =>
        HasAny
        && OutputCacheEnabled is null
        && FusionCacheEnabled is null
        && BypassWhenAuthenticated is null
        && VaryOutputCacheByUser is null
        && ETagMode is null
        && ClientCacheability is null
        && ScheduledUpdateUtc is null
        && ClientMustRevalidateNearUpdate is null
        && FusionCacheEagerRefreshRatio is null
        && FusionCacheJitterSeconds is null
        && FusionCacheFactorySoftTimeoutSeconds is null
        && FusionCacheFactoryHardTimeoutSeconds is null
        && FusionCacheMaxItemBytes is null
        && FusionCacheRespectNoStore is null
        && FusionCacheAllowBackgroundDistributed is null
        && FusionCacheAllowBackgroundBackplane is null
        && FusionCacheVaryOnPublicAddress is null
        && FusionCacheVaryOnEncoding is null
        && OutputCacheVaryByHost is null
        && (OutputCacheTtlSeconds is not null
            || FusionCacheSoftTtlSeconds is not null
            || FusionCacheHardTtlSeconds is not null
            || FusionCacheFailSafeSeconds is not null
            || ClientTtlSeconds is not null
            || ClientTtlMinSeconds is not null);
}

/// <summary>
/// Obsolete alias for <see cref="DomainSettingsPatch"/> (TTL-focused name).
/// Prefer <see cref="DomainSettingsPatch"/>.
/// </summary>
[Obsolete("Use DomainSettingsPatch. DomainTtlPatch remains for source compatibility.")]
public sealed class DomainTtlPatch
{
    /// <inheritdoc cref="DomainSettingsPatch.OutputCacheTtlSeconds"/>
    public int? OutputCacheTtlSeconds { get; init; }

    /// <inheritdoc cref="DomainSettingsPatch.FusionCacheSoftTtlSeconds"/>
    public int? FusionCacheSoftTtlSeconds { get; init; }

    /// <inheritdoc cref="DomainSettingsPatch.FusionCacheHardTtlSeconds"/>
    public int? FusionCacheHardTtlSeconds { get; init; }

    /// <inheritdoc cref="DomainSettingsPatch.FusionCacheFailSafeSeconds"/>
    public int? FusionCacheFailSafeSeconds { get; init; }

    /// <inheritdoc cref="DomainSettingsPatch.ClientTtlSeconds"/>
    public int? ClientTtlSeconds { get; init; }

    /// <inheritdoc cref="DomainSettingsPatch.ClientTtlMinSeconds"/>
    public int? ClientTtlMinSeconds { get; init; }

    /// <inheritdoc cref="DomainSettingsPatch.HasAny"/>
    public bool HasAny => ToSettingsPatch().HasAny;

    /// <summary>Maps to <see cref="DomainSettingsPatch"/>.</summary>
    public DomainSettingsPatch ToSettingsPatch() => new()
    {
        OutputCacheTtlSeconds = OutputCacheTtlSeconds,
        FusionCacheSoftTtlSeconds = FusionCacheSoftTtlSeconds,
        FusionCacheHardTtlSeconds = FusionCacheHardTtlSeconds,
        FusionCacheFailSafeSeconds = FusionCacheFailSafeSeconds,
        ClientTtlSeconds = ClientTtlSeconds,
        ClientTtlMinSeconds = ClientTtlMinSeconds,
    };
}
