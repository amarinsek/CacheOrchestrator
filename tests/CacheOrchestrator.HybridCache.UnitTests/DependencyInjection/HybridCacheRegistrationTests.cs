using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.HybridCache.UnitTests.DependencyInjection;

public class HybridCacheRegistrationTests
{
    [Fact]
    public void AddCacheOrchestratorHybridCache_ReplacesPriorDataCacheProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<Microsoft.Extensions.Caching.Hybrid.HybridCache>());

        // Simulate AspNetCore registering Fusion first (TryAdd).
        services.AddCacheOrchestratorFusionCache();
        services.AddCacheOrchestratorHybridCache();

        using ServiceProvider sp = services.BuildServiceProvider();
        IDataCacheProvider provider = sp.GetRequiredService<IDataCacheProvider>();
        provider.Name.Should().Be("HybridCache");
    }
}
