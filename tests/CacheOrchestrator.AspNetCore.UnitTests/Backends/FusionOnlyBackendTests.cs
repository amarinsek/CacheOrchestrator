using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache.Backends;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.AspNetCore.UnitTests.Backends;

public class FusionOnlyBackendTests
{
    private sealed class FusionOnlyL2Registrar : IFusionCacheBackendRegistrar
    {
        public string Name => "FusionOnlyDb";

        public void RegisterFusionCache(FusionCacheRegistrationContext context)
        {
            // No L2 — just prove the hook runs.
        }

    }

    [Fact]
    public void AddCacheOrchestrator_WhenFusionOnlyUsedForFusion_Succeeds()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "FusionOnlyDb"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        Action act = () =>
        {
            services.AddCacheOrchestratorAspNetCore(config);
            services.AddFusionCacheBackend(new FusionOnlyL2Registrar());
            services.AddCacheOrchestratorFusionCache(config);
        };

        act.Should().NotThrow();
    }
}
