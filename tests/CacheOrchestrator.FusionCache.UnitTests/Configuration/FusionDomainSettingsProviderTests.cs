using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.FusionCache.UnitTests.Configuration;

public sealed class FusionDomainSettingsProviderTests
{
    [Fact]
    public void Get_ReusesEffectiveSettingsUntilConfigurationChanges()
    {
        IConfigurationRoot configuration = BuildConfiguration();
        using FusionDomainSettingsProvider provider = CreateProvider(configuration);

        DomainFusionCacheSettings first = provider.Get("catalog");
        DomainFusionCacheSettings second = provider.Get("catalog");

        second.Should().BeSameAs(first);
        configuration["Cache:Domains:catalog:FusionCache:JitterSeconds"] = "12";
        configuration.Reload();

        DomainFusionCacheSettings reloaded = provider.Get("catalog");
        reloaded.Should().NotBeSameAs(first);
        reloaded.JitterSeconds.Should().Be(12);
    }

    [Fact]
    public void Get_RebuildsEffectiveSettingsWhenRuntimeOverrideStampChanges()
    {
        IConfigurationRoot configuration = BuildConfiguration();
        var overrides = new FusionDomainRuntimeOverrideStore();
        using FusionDomainSettingsProvider provider = CreateProvider(configuration, overrides);
        DomainFusionCacheSettings first = provider.Get("catalog");

        overrides.PatchSettings(
            "catalog",
            new FusionDomainSettingsPatch { Jitter = TimeSpan.FromSeconds(17) });

        DomainFusionCacheSettings updated = provider.Get("catalog");
        updated.Should().NotBeSameAs(first);
        updated.JitterSeconds.Should().Be(17);
    }

    private static IConfigurationRoot BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:DomainDefaults:FusionCache:JitterSeconds"] = "3",
                ["Cache:Domains:catalog:FusionCache:EagerRefreshRatio"] = "0.8"
            })
            .Build();

    private static FusionDomainSettingsProvider CreateProvider(
        IConfiguration configuration,
        IFusionDomainRuntimeOverrideStore? overrides = null) =>
        new(
            configuration,
            Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>(),
            overrides,
            "Cache");
}
