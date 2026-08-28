using CacheOrchestrator.Configuration;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AspNetCore.UnitTests.Performance;

public sealed class HotPathAllocationBudgetTests
{
    [Fact]
    public void ConfiguredDynamicDomainResolution_StaysWithinAllocationBudget()
    {
        IOptionsMonitor<CacheOrchestratorOptions> options =
            Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            Domains = { ["tiles-osm"] = new CacheOrchestratorOptions.DomainCacheSettings() }
        });
        ServiceCollection services = new();
        services.AddSingleton(options);
        using ServiceProvider provider = services.BuildServiceProvider();
        DefaultHttpContext http = new() { RequestServices = provider };
        DomainOutputCachePolicy policy = new(_ => "tiles-osm");

        for (int i = 0; i < 100; i++)
            policy.ResolveDomain(http);

        const int operations = 1_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < operations; i++)
            policy.ResolveDomain(http);
        long bytesPerOperation = (GC.GetAllocatedBytesForCurrentThread() - before) / operations;

        // Deliberately generous: this is a CI regression tripwire, not a replacement for BDN.
        bytesPerOperation.Should().BeLessThanOrEqualTo(512);
    }
}
