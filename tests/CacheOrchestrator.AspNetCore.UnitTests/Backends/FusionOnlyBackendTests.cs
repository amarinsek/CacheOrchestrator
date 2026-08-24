using CacheOrchestrator.Backends;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AspNetCore.UnitTests.Backends;

public class FusionOnlyBackendTests
{
    private sealed class FusionOnlyRegistrar : ICacheBackendRegistrar
    {
        public string Name => "FusionOnlyDb";
        public bool SupportsOutputCacheStore => false;

        public void RegisterOutputCache(OutputCacheRegistrationContext context) =>
            throw new InvalidOperationException("Should not be called for Output Cache.");

        public void RegisterFusionCache(FusionCacheRegistrationContext context)
        {
            // No L2 â€” just prove the hook runs.
        }

        public void RegisterHealthProbes(BackendHealthRegistrationContext context)
        {
        }
    }

    [Fact]
    public void AddCacheOrchestrator_WhenOutputProviderIsFusionOnly_ThrowsAtRegistration()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "FusionOnlyDb",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddCacheOrchestrator(config, o => o.AddBackend(new FusionOnlyRegistrar()));

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*does not support an Output Cache store*");
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

        var act = () => services.AddCacheOrchestrator(config, o => o.AddBackend(new FusionOnlyRegistrar()));

        act.Should().NotThrow();
    }

    [Fact]
    public void OptionsValidator_WhenOutputProviderFusionOnly_FailsValidate()
    {
        var validator = new CacheOrchestratorOptionsValidator(
            ["InMemory", "FusionOnlyDb"],
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["InMemory"] = true,
                ["FusionOnlyDb"] = false
            });

        var options = new CacheOrchestratorOptions
        {
            OutputCache = { Provider = "FusionOnlyDb" }
        };

        ValidateOptionsResult result = validator.Validate(null, options);
        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(f => f.Contains("does not support an Output Cache store", StringComparison.Ordinal));
    }
}
