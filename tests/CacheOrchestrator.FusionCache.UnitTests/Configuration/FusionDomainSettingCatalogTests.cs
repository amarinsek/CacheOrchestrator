using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.FusionCache.UnitTests.Configuration;

public class FusionDomainSettingCatalogTests
{
    [Fact]
    public void AddCacheOrchestratorFusionCache_registers_fusionCache_catalog_section()
    {
        ServiceCollection services = new();
        services.AddCacheOrchestratorFusionCache();

        IReadOnlyList<DomainSettingCatalogEntry> all = DomainSettingCatalog.GetEntries();
        Assert.Contains(all, e => e.Id == "fusionCache.hardTtl" && e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "fusionCache.failSafe" && e.RuntimeOverlay);
        Assert.Contains(all, e => e.Id == "fusionCache.eagerRefreshRatio" && e.RuntimeOverlay);
    }
}
