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

    /// <summary>Override data cache enabled.</summary>
    public bool? DataCacheEnabled { get; init; }

    /// <summary>Override auth bypass mode.</summary>
    public Configuration.AuthBypassMode? AuthBypassMode { get; init; }

    /// <summary>Override vary-by-user.</summary>
    public bool? VaryOutputCacheByUser { get; init; }

    /// <summary>Override treat-Authorization-as-auth-signal.</summary>
    public bool? TreatAuthorizationAsAuthSignal { get; init; }

    /// <summary>Override auth vary Authorization hash.</summary>
    public bool? AuthVaryIncludeAuthorizationHash { get; init; }

    /// <summary>Override data cache respects auth bypass.</summary>
    public bool? DataCacheRespectAuthBypass { get; init; }

    /// <summary>Override force-private-when-authenticated.</summary>
    public bool? ClientForcePrivateWhenAuthenticated { get; init; }

    /// <summary>Override vary-by-Accept.</summary>
    public bool? VaryByAccept { get; init; }

    /// <summary>Override vary-by-Accept-Language.</summary>
    public bool? VaryByAcceptLanguage { get; init; }

    /// <summary>Override emit response Vary.</summary>
    public bool? EmitResponseVary { get; init; }

    /// <summary>Override Accept normalization list (<see langword="null"/> = inherit).</summary>
    public string[]? AcceptNormalizationList { get; init; }

    /// <summary>Override Accept-Language normalization list.</summary>
    public string[]? AcceptLanguageNormalizationList { get; init; }

    /// <summary>Override VaryByHeaders allowlist.</summary>
    public string[]? VaryByHeaders { get; init; }

    /// <summary>Override VaryByQueryKeys allowlist.</summary>
    public string[]? VaryByQueryKeys { get; init; }

    /// <summary>Override IgnoreQueryKeys deny list.</summary>
    public string[]? IgnoreQueryKeys { get; init; }

    /// <summary>Override VaryByCookies allowlist.</summary>
    public string[]? VaryByCookies { get; init; }

    /// <summary>Override VaryByAuthClaims list.</summary>
    public string[]? VaryByAuthClaims { get; init; }

    /// <summary>Override ETag mode.</summary>
    public Configuration.ETagMode? ETagMode { get; init; }

    /// <summary>Override client cacheability.</summary>
    public Configuration.ClientCacheability? ClientCacheability { get; init; }

    /// <summary>Override client TTL.</summary>
    public TimeSpan? ClientTtl { get; init; }

    /// <summary>Override client TTL min.</summary>
    public TimeSpan? ClientTtlMin { get; init; }

    /// <summary>Override scheduled update UTC.</summary>
    public DateTimeOffset? ScheduledUpdateUtc { get; init; }

    /// <summary>Override must-revalidate near update.</summary>
    public bool? ClientMustRevalidateNearUpdate { get; init; }

    /// <summary>Override Output Cache TTL.</summary>
    public TimeSpan? OutputCacheTtl { get; init; }

    /// <summary>Override data cache TTL.</summary>
    public TimeSpan? DataCacheTtl { get; init; }

    /// <summary>Override respect no-store.</summary>
    public bool? DataCacheRespectNoStore { get; init; }

    /// <summary>Override vary on public address.</summary>
    public bool? DataCacheVaryOnPublicAddress { get; init; }

    /// <summary>Override vary on encoding.</summary>
    public bool? DataCacheVaryOnEncoding { get; init; }

    /// <summary>Override Output Cache vary-by-host.</summary>
    public bool? OutputCacheVaryByHost { get; init; }

    /// <summary>True when any field is set.</summary>
    public bool HasAny =>
        Version is not null
        || OutputCacheEnabled is not null
        || DataCacheEnabled is not null
        || AuthBypassMode is not null
        || VaryOutputCacheByUser is not null
        || TreatAuthorizationAsAuthSignal is not null
        || AuthVaryIncludeAuthorizationHash is not null
        || DataCacheRespectAuthBypass is not null
        || ClientForcePrivateWhenAuthenticated is not null
        || VaryByAccept is not null
        || VaryByAcceptLanguage is not null
        || EmitResponseVary is not null
        || AcceptNormalizationList is not null
        || AcceptLanguageNormalizationList is not null
        || VaryByHeaders is not null
        || VaryByQueryKeys is not null
        || IgnoreQueryKeys is not null
        || VaryByCookies is not null
        || VaryByAuthClaims is not null
        || ETagMode is not null
        || ClientCacheability is not null
        || ClientTtl is not null
        || ClientTtlMin is not null
        || ScheduledUpdateUtc is not null
        || ClientMustRevalidateNearUpdate is not null
        || OutputCacheTtl is not null
        || DataCacheTtl is not null
        || DataCacheRespectNoStore is not null
        || DataCacheVaryOnPublicAddress is not null
        || DataCacheVaryOnEncoding is not null
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

    /// <summary>Data cache enabled.</summary>
    public bool? DataCacheEnabled { get; init; }

    /// <summary>Auth bypass mode.</summary>
    public Configuration.AuthBypassMode? AuthBypassMode { get; init; }

    /// <summary>Vary Output Cache by user.</summary>
    public bool? VaryOutputCacheByUser { get; init; }

    /// <summary>Treat Authorization as auth signal.</summary>
    public bool? TreatAuthorizationAsAuthSignal { get; init; }

    /// <summary>Auth vary include Authorization hash.</summary>
    public bool? AuthVaryIncludeAuthorizationHash { get; init; }

    /// <summary>Data cache respects auth bypass.</summary>
    public bool? DataCacheRespectAuthBypass { get; init; }

    /// <summary>Force private when authenticated.</summary>
    public bool? ClientForcePrivateWhenAuthenticated { get; init; }

    /// <summary>Vary by Accept.</summary>
    public bool? VaryByAccept { get; init; }

    /// <summary>Vary by Accept-Language.</summary>
    public bool? VaryByAcceptLanguage { get; init; }

    /// <summary>Emit response Vary.</summary>
    public bool? EmitResponseVary { get; init; }

    /// <summary>Accept normalization list.</summary>
    public string[]? AcceptNormalizationList { get; init; }

    /// <summary>Accept-Language normalization list.</summary>
    public string[]? AcceptLanguageNormalizationList { get; init; }

    /// <summary>Vary by headers.</summary>
    public string[]? VaryByHeaders { get; init; }

    /// <summary>Vary by query keys.</summary>
    public string[]? VaryByQueryKeys { get; init; }

    /// <summary>Ignore query keys.</summary>
    public string[]? IgnoreQueryKeys { get; init; }

    /// <summary>Vary by cookies.</summary>
    public string[]? VaryByCookies { get; init; }

    /// <summary>Vary by auth claims.</summary>
    public string[]? VaryByAuthClaims { get; init; }

    /// <summary>ETag mode.</summary>
    public Configuration.ETagMode? ETagMode { get; init; }

    /// <summary>Client cacheability.</summary>
    public Configuration.ClientCacheability? ClientCacheability { get; init; }

    /// <summary>Client TTL.</summary>
    public TimeSpan? ClientTtl { get; init; }

    /// <summary>Client TTL min.</summary>
    public TimeSpan? ClientTtlMin { get; init; }

    /// <summary>Scheduled update UTC.</summary>
    public DateTimeOffset? ScheduledUpdateUtc { get; init; }

    /// <summary>Must-revalidate near update.</summary>
    public bool? ClientMustRevalidateNearUpdate { get; init; }

    /// <summary>Output Cache TTL.</summary>
    public TimeSpan? OutputCacheTtl { get; init; }

    /// <summary>Data cache TTL.</summary>
    public TimeSpan? DataCacheTtl { get; init; }

    /// <summary>Respect no-store.</summary>
    public bool? DataCacheRespectNoStore { get; init; }

    /// <summary>Vary on public address.</summary>
    public bool? DataCacheVaryOnPublicAddress { get; init; }

    /// <summary>Vary on encoding.</summary>
    public bool? DataCacheVaryOnEncoding { get; init; }

    /// <summary>Vary Output Cache by host.</summary>
    public bool? OutputCacheVaryByHost { get; init; }

    /// <summary>True when at least one field is provided.</summary>
    public bool HasAny =>
        OutputCacheEnabled is not null
        || DataCacheEnabled is not null
        || AuthBypassMode is not null
        || VaryOutputCacheByUser is not null
        || TreatAuthorizationAsAuthSignal is not null
        || AuthVaryIncludeAuthorizationHash is not null
        || DataCacheRespectAuthBypass is not null
        || ClientForcePrivateWhenAuthenticated is not null
        || VaryByAccept is not null
        || VaryByAcceptLanguage is not null
        || EmitResponseVary is not null
        || AcceptNormalizationList is not null
        || AcceptLanguageNormalizationList is not null
        || VaryByHeaders is not null
        || VaryByQueryKeys is not null
        || IgnoreQueryKeys is not null
        || VaryByCookies is not null
        || VaryByAuthClaims is not null
        || ETagMode is not null
        || ClientCacheability is not null
        || ClientTtl is not null
        || ClientTtlMin is not null
        || ScheduledUpdateUtc is not null
        || ClientMustRevalidateNearUpdate is not null
        || OutputCacheTtl is not null
        || DataCacheTtl is not null
        || DataCacheRespectNoStore is not null
        || DataCacheVaryOnPublicAddress is not null
        || DataCacheVaryOnEncoding is not null
        || OutputCacheVaryByHost is not null;

    /// <summary>True when this patch only touches portable TTL fields (safe for old <c>ttlPatch</c> peers).</summary>
    public bool IsTtlOnly =>
        HasAny
        && OutputCacheEnabled is null
        && DataCacheEnabled is null
        && AuthBypassMode is null
        && VaryOutputCacheByUser is null
        && TreatAuthorizationAsAuthSignal is null
        && AuthVaryIncludeAuthorizationHash is null
        && DataCacheRespectAuthBypass is null
        && ClientForcePrivateWhenAuthenticated is null
        && VaryByAccept is null
        && VaryByAcceptLanguage is null
        && EmitResponseVary is null
        && AcceptNormalizationList is null
        && AcceptLanguageNormalizationList is null
        && VaryByHeaders is null
        && VaryByQueryKeys is null
        && IgnoreQueryKeys is null
        && VaryByCookies is null
        && VaryByAuthClaims is null
        && ETagMode is null
        && ClientCacheability is null
        && ScheduledUpdateUtc is null
        && ClientMustRevalidateNearUpdate is null
        && DataCacheRespectNoStore is null
        && DataCacheVaryOnPublicAddress is null
        && DataCacheVaryOnEncoding is null
        && OutputCacheVaryByHost is null
        && (OutputCacheTtl is not null
            || DataCacheTtl is not null
            || ClientTtl is not null
            || ClientTtlMin is not null);
}

/// <summary>
/// Obsolete alias for <see cref="DomainSettingsPatch"/> (TTL-focused name).
/// Prefer <see cref="DomainSettingsPatch"/>.
/// </summary>
[Obsolete("Use DomainSettingsPatch. DomainTtlPatch remains for source compatibility.")]
public sealed class DomainTtlPatch
{
    /// <summary>Output Cache TTL seconds.</summary>
    public int? OutputCacheTtlSeconds { get; init; }

    /// <summary>Data-cache soft TTL seconds.</summary>
    public int? DataCacheTtlSeconds { get; init; }

    /// <summary>Client TTL seconds.</summary>
    public int? ClientTtlSeconds { get; init; }

    /// <summary>Client TTL min seconds.</summary>
    public int? ClientTtlMinSeconds { get; init; }

    /// <inheritdoc cref="DomainSettingsPatch.HasAny"/>
    public bool HasAny => ToSettingsPatch().HasAny;

    /// <summary>Maps to <see cref="DomainSettingsPatch"/>.</summary>
    public DomainSettingsPatch ToSettingsPatch() => new()
    {
        OutputCacheTtl = FromSeconds(OutputCacheTtlSeconds),
        DataCacheTtl = FromSeconds(DataCacheTtlSeconds),
        ClientTtl = FromSeconds(ClientTtlSeconds),
        ClientTtlMin = FromSeconds(ClientTtlMinSeconds),
    };

    private static TimeSpan? FromSeconds(int? seconds) =>
        seconds is int s ? TimeSpan.FromSeconds(s) : null;
}
