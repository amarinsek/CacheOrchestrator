using CacheOrchestrator.Admin;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.FusionCache.UnitTests.DependencyInjection;

public sealed class CoreWorkerRegistrationTests
{
    [Fact]
    public async Task CoreAndFusionCache_RegisterHttpFreeWorkerServices()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
                ["Cache:Domains:catalog:Version"] = "1",
                ["Cache:Domains:catalog:DataCache:Enabled"] = "true",
                ["Cache:Domains:catalog:DataCache:TtlSeconds"] = "300",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestratorCore(configuration);
        services.AddCacheOrchestratorFusionCache(configuration);

        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        ICacheOrchestrator cache = provider.GetRequiredService<ICacheOrchestrator>();
        ICacheOrchestratorManagement management =
            provider.GetRequiredService<ICacheOrchestratorManagement>();
        provider.GetRequiredService<IDataCacheProvider>().Name.Should().Be("FusionCache");
        provider.GetRequiredService<ICacheOrchestratorInvalidator>().Should().NotBeNull();

        AdminDomainConfigDto initial = management.GetDomain("catalog")!;
        initial.DataCacheEnabled.Should().BeTrue();
        initial.DataCacheTtlSeconds.Should().Be(300);
        initial.OutputCacheEnabled.Should().BeFalse();

        AdminDomainMutationResultDto version = await management.SetVersionAsync(
            "catalog",
            new AdminVersionRequest { Version = "worker-v2" },
            TestContext.Current.CancellationToken);
        version.Effective.Version.Should().Be("worker-v2");
        version.Effective.VersionIsRuntimeOverride.Should().BeTrue();

        int factoryRuns = 0;
        async ValueTask<string?> Factory(CancellationToken cancellationToken)
        {
            factoryRuns++;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return "value";
        }

        string? first = await cache.GetOrCreateAsync(
            new CacheEntryRequest { Domain = "catalog", Key = "product:42" },
            Factory,
            TestContext.Current.CancellationToken);
        string? second = await cache.GetOrCreateAsync(
            new CacheEntryRequest { Domain = "catalog", Key = "product:42" },
            Factory,
            TestContext.Current.CancellationToken);

        first.Should().Be("value");
        second.Should().Be("value");
        factoryRuns.Should().Be(1);
    }
}
