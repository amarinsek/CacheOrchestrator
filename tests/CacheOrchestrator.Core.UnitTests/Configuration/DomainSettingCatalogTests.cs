using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Core.UnitTests.Configuration;

public class DomainSettingCatalogTests
{
    [Fact]
    public void GetEntries_includes_attributed_domain_settings()
    {
        IReadOnlyList<DomainSettingCatalogEntry> all = DomainSettingCatalog.GetEntries();
        Assert.NotEmpty(all);
        Assert.Contains(all, e => e.Id == "outputCache.ttlSeconds" && e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "dataCache.enabled" && e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "dataCache.ttlSeconds" && e.RuntimeOverlay);
        Assert.DoesNotContain(all, e => e.Id == "dataCache.hardTtl");
        Assert.DoesNotContain(all, e => e.Id == "dataCache.failSafe");
        Assert.Contains(all, e => e.Id == "clientCache.scheduledUpdateUtc" && e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "dataCache.instance" && !e.RuntimeOverlay);
        Assert.DoesNotContain(all, e => e.Id.StartsWith("fusionCache.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, e => e.Id == "version" && !e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "authBypassMode" && e.RuntimeOverlay);
    }

    [Fact]
    public void GetOverlayEntries_only_runtime_overlay()
    {
        IReadOnlyList<DomainSettingCatalogEntry> overlay = DomainSettingCatalog.GetOverlayEntries();
        Assert.NotEmpty(overlay);
        Assert.All(overlay, e => Assert.True(e.RuntimeOverlay));
        Assert.DoesNotContain(overlay, e => e.Id == "dataCache.instance");
        Assert.DoesNotContain(overlay, e => e.Id == "version");
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        DomainSettingCatalogEntry? a = DomainSettingCatalog.Find("outputCache.ttlSeconds");
        DomainSettingCatalogEntry? b = DomainSettingCatalog.Find("outputCache.ttlSeconds");
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a.Id, b.Id);
    }
}
