using CacheOrchestrator.Backends;
using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.AspNetCore.UnitTests.Backends;

public class InMemoryCacheBackendRegistrarTests
{
    private readonly InMemoryCacheBackendRegistrar _sut = new();

    [Fact]
    public void Name_IsInMemory() => _sut.Name.Should().Be("InMemory");

    [Fact]
    public void SupportsOutputCacheStore_IsTrue() => _sut.SupportsOutputCacheStore.Should().BeTrue();

    [Fact]
    public void RegisterOutputCache_ConfiguresInMemorySizeLimits()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var options = new CacheOrchestratorOptions();
        List<Action<OutputCacheOptions>> configurators = [];
        var context = new OutputCacheRegistrationContext(
            services, configuration, options, "Cache", "InMemory", configurators);

        _sut.RegisterOutputCache(context);

        var oc = new OutputCacheOptions();
        foreach (Action<OutputCacheOptions> configure in configurators)
            configure(oc);

        oc.SizeLimit.Should().Be(512 * 1024 * 1024);
        oc.MaximumBodySize.Should().Be(32 * 1024 * 1024);
    }

    [Fact]
    public void RegisterHealthProbes_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var options = new CacheOrchestratorOptions();
        var context = new BackendHealthRegistrationContext(
            services, configuration, "Cache", "oc", "InMemory", options,
            new CacheOrchestratorOptions.DataCacheInstanceOptions());

        var act = () => _sut.RegisterHealthProbes(context);
        act.Should().NotThrow();
    }
}
