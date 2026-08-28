using CacheOrchestrator.Backends;
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
    public void RegisterOutputCache_ConfiguresInMemorySizeLimits()
    {
        var services = new ServiceCollection();
        IConfigurationRoot configuration = new ConfigurationBuilder().Build();
        List<Action<OutputCacheOptions>> configurators = [];
        var context = new OutputCacheRegistrationContext(
            services, configuration, "test-oc", "Cache", "InMemory", configurators);

        _sut.RegisterOutputCache(context);

        var oc = new OutputCacheOptions();
        foreach (Action<OutputCacheOptions> configure in configurators)
            configure(oc);

        oc.SizeLimit.Should().Be(512 * 1024 * 1024);
        oc.MaximumBodySize.Should().Be(32 * 1024 * 1024);
    }
}
