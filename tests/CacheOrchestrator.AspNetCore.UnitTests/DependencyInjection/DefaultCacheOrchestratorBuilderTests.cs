using CacheOrchestrator.Backends;
using CacheOrchestrator.DependencyInjection;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.AspNetCore.UnitTests.DependencyInjection;

public class DefaultCacheOrchestratorBuilderTests
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly IConfiguration _configuration = new ConfigurationBuilder().Build();

    [Fact]
    public void Constructor_SetsServicesAndConfiguration()
    {
        var builder = new DefaultCacheOrchestratorBuilder(_services, _configuration);

        builder.Services.Should().BeSameAs(_services);
        builder.Configuration.Should().BeSameAs(_configuration);
    }

    [Fact]
    public void AddBackend_WhenNull_ThrowsArgumentNullException()
    {
        var builder = new DefaultCacheOrchestratorBuilder(_services, _configuration);

        var act = () => builder.AddBackend(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registrar");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AddBackend_WhenNameIsNullOrWhiteSpace_ThrowsArgumentException(string? name)
    {
        var builder = new DefaultCacheOrchestratorBuilder(_services, _configuration);
        var mockRegistrar = Substitute.For<ICacheBackendRegistrar>();
        mockRegistrar.Name.Returns(name);

        var act = () => builder.AddBackend(mockRegistrar);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*Registrar Name cannot be null or empty*")
           .WithParameterName("registrar");
    }

    [Fact]
    public void AddBackend_RegistersBackend()
    {
        var builder = new DefaultCacheOrchestratorBuilder(_services, _configuration);
        var mockRegistrar = Substitute.For<ICacheBackendRegistrar>();
        mockRegistrar.Name.Returns("CustomDB");

        builder.AddBackend(mockRegistrar);

        var names = builder.GetRegisteredProviderNames();
        names.Should().ContainSingle().Which.Should().Be("CustomDB");
    }

    [Fact]
    public void ResolveRegistrar_WhenRegistered_ReturnsInstance()
    {
        var builder = new DefaultCacheOrchestratorBuilder(_services, _configuration);
        var mockRegistrar = Substitute.For<ICacheBackendRegistrar>();
        mockRegistrar.Name.Returns("CustomDB");

        builder.AddBackend(mockRegistrar);

        var resolved = builder.ResolveRegistrar("CustomDB");

        resolved.Should().BeSameAs(mockRegistrar);
    }

    [Fact]
    public void ResolveRegistrar_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var builder = new DefaultCacheOrchestratorBuilder(_services, _configuration);

        var act = () => builder.ResolveRegistrar("MissingDB");

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Unsupported cache provider 'MissingDB'*");
    }

    [Fact]
    public void ConfigureOutputCache_StoresCallback()
    {
        var builder = new DefaultCacheOrchestratorBuilder(_services, _configuration);
        bool called = false;

        builder.ConfigureOutputCache(_ => called = true);

        builder.OutputCacheConfigurators.Should().ContainSingle();
        var options = new OutputCacheOptions();
        builder.OutputCacheConfigurators[0](options);
        called.Should().BeTrue();
    }
}