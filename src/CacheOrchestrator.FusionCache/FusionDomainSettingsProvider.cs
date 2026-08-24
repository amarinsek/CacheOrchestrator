using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.FusionCache;

/// <summary>
/// Reads <c>Cache:DomainDefaults:FusionCache</c> and <c>Cache:Domains:{name}:FusionCache</c>,
/// then merges Admin runtime overlays from <see cref="IFusionDomainRuntimeOverrideStore"/>.
/// </summary>
internal sealed class FusionDomainSettingsProvider : IFusionDomainSettingsProvider
{
    private readonly IConfiguration _configuration;
    private readonly IFusionDomainRuntimeOverrideStore _runtimeOverrides;
    private readonly string _configSection;

    public FusionDomainSettingsProvider(
        IConfiguration configuration,
        IOptionsMonitor<CacheOrchestratorOptions> options,
        IFusionDomainRuntimeOverrideStore? runtimeOverrides = null,
        string configSection = "Cache")
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        _configuration = configuration;
        _runtimeOverrides = runtimeOverrides ?? NullFusionDomainRuntimeOverrideStore.Instance;
        _configSection = string.IsNullOrWhiteSpace(configSection) ? "Cache" : configSection;
    }

    /// <inheritdoc />
    public DomainFusionCacheSettings Get(string domain)
    {
        domain = DomainName.Normalize(domain);

        DomainFusionCacheSettings defaults = BindSection($"{_configSection}:DomainDefaults:FusionCache");
        DomainFusionCacheSettings specific = BindSection($"{_configSection}:Domains:{domain}:FusionCache");
        FusionDomainRuntimeOverride? overlay = _runtimeOverrides.Get(domain);

        static T Pick<T>(T? specific, T? global, T fallback) where T : struct =>
            specific ?? global ?? fallback;

        TimeSpan hardTtl = overlay?.HardTtl
            ?? Pick(specific.HardTtl, defaults.HardTtl, TimeSpan.FromSeconds(43200));
        TimeSpan failSafe = overlay?.FailSafe
            ?? Pick(specific.FailSafe, defaults.FailSafe, TimeSpan.FromSeconds(86400));
        TimeSpan jitter = overlay?.Jitter
            ?? Pick(specific.Jitter, defaults.Jitter, TimeSpan.FromSeconds(60));
        TimeSpan factorySoft = overlay?.FactorySoftTimeout
            ?? Pick(specific.FactorySoftTimeout, defaults.FactorySoftTimeout, TimeSpan.FromSeconds(1));
        TimeSpan factoryHard = overlay?.FactoryHardTimeout
            ?? Pick(specific.FactoryHardTimeout, defaults.FactoryHardTimeout, TimeSpan.FromSeconds(5));

        return new DomainFusionCacheSettings
        {
            HardTtl = hardTtl < TimeSpan.Zero ? TimeSpan.Zero : hardTtl,
            FailSafe = failSafe < TimeSpan.Zero ? TimeSpan.Zero : failSafe,
            EagerRefreshRatio = overlay?.EagerRefreshRatio
                ?? Pick(specific.EagerRefreshRatio, defaults.EagerRefreshRatio, 0.9),
            Jitter = jitter < TimeSpan.Zero ? TimeSpan.Zero : jitter,
            FactorySoftTimeout = factorySoft < TimeSpan.Zero ? TimeSpan.Zero : factorySoft,
            FactoryHardTimeout = factoryHard < TimeSpan.Zero ? TimeSpan.Zero : factoryHard,
            MaxItemBytes = overlay?.MaxItemBytes
                ?? Pick(specific.MaxItemBytes, defaults.MaxItemBytes, 0),
            AllowBackgroundDistributed = overlay?.AllowBackgroundDistributed
                ?? Pick(specific.AllowBackgroundDistributed, defaults.AllowBackgroundDistributed, true),
            AllowBackgroundBackplane = overlay?.AllowBackgroundBackplane
                ?? Pick(specific.AllowBackgroundBackplane, defaults.AllowBackgroundBackplane, true),
        };
    }

    private DomainFusionCacheSettings BindSection(string path)
    {
        DomainFusionCacheSettings settings = new();
        _configuration.GetSection(path).Bind(settings);
        return settings;
    }
}
