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
    [Obsolete("Use PatchSettings.")]
    public DomainRuntimeOverride PatchTtl(string domain, DomainTtlPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return PatchSettings(domain, patch.ToSettingsPatch());
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
            OutputCacheEnabled = patch.OutputCacheEnabled ?? existing.OutputCacheEnabled,
            FusionCacheEnabled = patch.FusionCacheEnabled ?? existing.FusionCacheEnabled,
            BypassWhenAuthenticated = patch.BypassWhenAuthenticated ?? existing.BypassWhenAuthenticated,
            AuthBypassMode = patch.AuthBypassMode ?? existing.AuthBypassMode,
            VaryOutputCacheByUser = patch.VaryOutputCacheByUser ?? existing.VaryOutputCacheByUser,
            TreatAuthorizationAsAuthSignal = patch.TreatAuthorizationAsAuthSignal ?? existing.TreatAuthorizationAsAuthSignal,
            AuthVaryIncludeAuthorizationHash = patch.AuthVaryIncludeAuthorizationHash ?? existing.AuthVaryIncludeAuthorizationHash,
            FusionRespectAuthBypass = patch.FusionRespectAuthBypass ?? existing.FusionRespectAuthBypass,
            ClientForcePrivateWhenAuthenticated = patch.ClientForcePrivateWhenAuthenticated ?? existing.ClientForcePrivateWhenAuthenticated,
            VaryByAccept = patch.VaryByAccept ?? existing.VaryByAccept,
            VaryByAcceptLanguage = patch.VaryByAcceptLanguage ?? existing.VaryByAcceptLanguage,
            EmitResponseVary = patch.EmitResponseVary ?? existing.EmitResponseVary,
            AcceptNormalizationList = patch.AcceptNormalizationList ?? existing.AcceptNormalizationList,
            AcceptLanguageNormalizationList = patch.AcceptLanguageNormalizationList ?? existing.AcceptLanguageNormalizationList,
            VaryByHeaders = patch.VaryByHeaders ?? existing.VaryByHeaders,
            VaryByQueryKeys = patch.VaryByQueryKeys ?? existing.VaryByQueryKeys,
            IgnoreQueryKeys = patch.IgnoreQueryKeys ?? existing.IgnoreQueryKeys,
            VaryByCookies = patch.VaryByCookies ?? existing.VaryByCookies,
            VaryByAuthClaims = patch.VaryByAuthClaims ?? existing.VaryByAuthClaims,
            ETagMode = patch.ETagMode ?? existing.ETagMode,
            ClientCacheability = patch.ClientCacheability ?? existing.ClientCacheability,
            ClientTtlSeconds = patch.ClientTtlSeconds ?? existing.ClientTtlSeconds,
            ClientTtlMinSeconds = patch.ClientTtlMinSeconds ?? existing.ClientTtlMinSeconds,
            ScheduledUpdateUtc = patch.ScheduledUpdateUtc ?? existing.ScheduledUpdateUtc,
            ClientMustRevalidateNearUpdate = patch.ClientMustRevalidateNearUpdate ?? existing.ClientMustRevalidateNearUpdate,
            OutputCacheTtlSeconds = patch.OutputCacheTtlSeconds ?? existing.OutputCacheTtlSeconds,
            FusionCacheSoftTtlSeconds = patch.FusionCacheSoftTtlSeconds ?? existing.FusionCacheSoftTtlSeconds,
            FusionCacheHardTtlSeconds = patch.FusionCacheHardTtlSeconds ?? existing.FusionCacheHardTtlSeconds,
            FusionCacheFailSafeSeconds = patch.FusionCacheFailSafeSeconds ?? existing.FusionCacheFailSafeSeconds,
            FusionCacheEagerRefreshRatio = patch.FusionCacheEagerRefreshRatio ?? existing.FusionCacheEagerRefreshRatio,
            FusionCacheJitterSeconds = patch.FusionCacheJitterSeconds ?? existing.FusionCacheJitterSeconds,
            FusionCacheFactorySoftTimeoutSeconds = patch.FusionCacheFactorySoftTimeoutSeconds ?? existing.FusionCacheFactorySoftTimeoutSeconds,
            FusionCacheFactoryHardTimeoutSeconds = patch.FusionCacheFactoryHardTimeoutSeconds ?? existing.FusionCacheFactoryHardTimeoutSeconds,
            FusionCacheMaxItemBytes = patch.FusionCacheMaxItemBytes ?? existing.FusionCacheMaxItemBytes,
            FusionCacheRespectNoStore = patch.FusionCacheRespectNoStore ?? existing.FusionCacheRespectNoStore,
            FusionCacheAllowBackgroundDistributed = patch.FusionCacheAllowBackgroundDistributed ?? existing.FusionCacheAllowBackgroundDistributed,
            FusionCacheAllowBackgroundBackplane = patch.FusionCacheAllowBackgroundBackplane ?? existing.FusionCacheAllowBackgroundBackplane,
            FusionCacheVaryOnPublicAddress = patch.FusionCacheVaryOnPublicAddress ?? existing.FusionCacheVaryOnPublicAddress,
            FusionCacheVaryOnEncoding = patch.FusionCacheVaryOnEncoding ?? existing.FusionCacheVaryOnEncoding,
            OutputCacheVaryByHost = patch.OutputCacheVaryByHost ?? existing.OutputCacheVaryByHost,
        };

    private static DomainRuntimeOverride WithVersion(DomainRuntimeOverride existing, string version, int stamp) =>
        new()
        {
            Stamp = stamp,
            Version = version,
            OutputCacheEnabled = existing.OutputCacheEnabled,
            FusionCacheEnabled = existing.FusionCacheEnabled,
            BypassWhenAuthenticated = existing.BypassWhenAuthenticated,
            AuthBypassMode = existing.AuthBypassMode,
            VaryOutputCacheByUser = existing.VaryOutputCacheByUser,
            TreatAuthorizationAsAuthSignal = existing.TreatAuthorizationAsAuthSignal,
            AuthVaryIncludeAuthorizationHash = existing.AuthVaryIncludeAuthorizationHash,
            FusionRespectAuthBypass = existing.FusionRespectAuthBypass,
            ClientForcePrivateWhenAuthenticated = existing.ClientForcePrivateWhenAuthenticated,
            VaryByAccept = existing.VaryByAccept,
            VaryByAcceptLanguage = existing.VaryByAcceptLanguage,
            EmitResponseVary = existing.EmitResponseVary,
            AcceptNormalizationList = existing.AcceptNormalizationList,
            AcceptLanguageNormalizationList = existing.AcceptLanguageNormalizationList,
            VaryByHeaders = existing.VaryByHeaders,
            VaryByQueryKeys = existing.VaryByQueryKeys,
            IgnoreQueryKeys = existing.IgnoreQueryKeys,
            VaryByCookies = existing.VaryByCookies,
            VaryByAuthClaims = existing.VaryByAuthClaims,
            ETagMode = existing.ETagMode,
            ClientCacheability = existing.ClientCacheability,
            ClientTtlSeconds = existing.ClientTtlSeconds,
            ClientTtlMinSeconds = existing.ClientTtlMinSeconds,
            ScheduledUpdateUtc = existing.ScheduledUpdateUtc,
            ClientMustRevalidateNearUpdate = existing.ClientMustRevalidateNearUpdate,
            OutputCacheTtlSeconds = existing.OutputCacheTtlSeconds,
            FusionCacheSoftTtlSeconds = existing.FusionCacheSoftTtlSeconds,
            FusionCacheHardTtlSeconds = existing.FusionCacheHardTtlSeconds,
            FusionCacheFailSafeSeconds = existing.FusionCacheFailSafeSeconds,
            FusionCacheEagerRefreshRatio = existing.FusionCacheEagerRefreshRatio,
            FusionCacheJitterSeconds = existing.FusionCacheJitterSeconds,
            FusionCacheFactorySoftTimeoutSeconds = existing.FusionCacheFactorySoftTimeoutSeconds,
            FusionCacheFactoryHardTimeoutSeconds = existing.FusionCacheFactoryHardTimeoutSeconds,
            FusionCacheMaxItemBytes = existing.FusionCacheMaxItemBytes,
            FusionCacheRespectNoStore = existing.FusionCacheRespectNoStore,
            FusionCacheAllowBackgroundDistributed = existing.FusionCacheAllowBackgroundDistributed,
            FusionCacheAllowBackgroundBackplane = existing.FusionCacheAllowBackgroundBackplane,
            FusionCacheVaryOnPublicAddress = existing.FusionCacheVaryOnPublicAddress,
            FusionCacheVaryOnEncoding = existing.FusionCacheVaryOnEncoding,
            OutputCacheVaryByHost = existing.OutputCacheVaryByHost,
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

    [Obsolete("Use PatchSettings.")]
    public DomainRuntimeOverride PatchTtl(string domain, DomainTtlPatch patch) =>
        throw new InvalidOperationException("Admin runtime overrides are disabled.");

    public bool Clear(string domain) => false;

    public IReadOnlyCollection<string> GetOverriddenDomains() => [];
}
