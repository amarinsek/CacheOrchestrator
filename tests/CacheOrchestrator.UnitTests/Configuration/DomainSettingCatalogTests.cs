using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.UnitTests.Configuration;

public class DomainSettingCatalogTests
{
    [Fact]
    public void GetEntries_includes_attributed_domain_settings()
    {
        IReadOnlyList<DomainSettingCatalogEntry> all = DomainSettingCatalog.GetEntries();
        Assert.NotEmpty(all);
        Assert.Contains(all, e => e.Id == "outputCacheTtlSeconds" && e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "fusionCacheEnabled" && e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "scheduledUpdateUtc" && e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "fusionCacheInstance" && !e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "version" && !e.RuntimeOverlay);
    }

    [Fact]
    public void GetOverlayEntries_only_runtime_overlay()
    {
        IReadOnlyList<DomainSettingCatalogEntry> overlay = DomainSettingCatalog.GetOverlayEntries();
        Assert.NotEmpty(overlay);
        Assert.All(overlay, e => Assert.True(e.RuntimeOverlay));
        Assert.DoesNotContain(overlay, e => e.Id == "fusionCacheInstance");
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        DomainSettingCatalogEntry? a = DomainSettingCatalog.Find("OutputCacheTtlSeconds");
        DomainSettingCatalogEntry? b = DomainSettingCatalog.Find("outputCacheTtlSeconds");
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a!.Id, b!.Id);
    }
}
