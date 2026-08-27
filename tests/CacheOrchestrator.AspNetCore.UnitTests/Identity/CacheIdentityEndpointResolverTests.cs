using CacheOrchestrator.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.AspNetCore.UnitTests.Identity;

public class CacheIdentityEndpointResolverTests
{
    [Fact]
    public void ResolveEndpoint_UnknownContract_Throws()
    {
        CacheIdentityEndpointMetadata metadata = new();
        metadata.AddBinding("POST", CacheIdentityBinding.CreateNamed("missing"), "test");

        Endpoint endpoint = new(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "test");

        CacheIdentityContractCatalog catalog = new([]);

        Action act = () => CacheIdentityEndpointResolver.ResolveEndpoint(endpoint, catalog);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing*");
    }

    [Fact]
    public void ResolveEndpoint_KnownContract_SetsInstance()
    {
        FixedContract contract = new("search-v1");
        var binding = CacheIdentityBinding.CreateNamed("search-v1");
        CacheIdentityEndpointMetadata metadata = new();
        metadata.AddBinding("POST", binding, "test");

        Endpoint endpoint = new(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "test");

        CacheIdentityContractCatalog catalog = new([contract]);
        CacheIdentityEndpointResolver.ResolveEndpoint(endpoint, catalog);

        metadata.IsResolved.Should().BeTrue();
        binding.Contract.Should().BeSameAs(contract);
    }

    [Fact]
    public void EnsureResolved_UsesCatalogFromServices()
    {
        FixedContract contract = new("search-v1");
        var binding = CacheIdentityBinding.CreateNamed("search-v1");
        CacheIdentityEndpointMetadata metadata = new();
        metadata.AddBinding("GET", binding, "test");

        Endpoint endpoint = new(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "test");

        ServiceCollection services = new();
        services.AddSingleton(new CacheIdentityContractCatalog([contract]));
        ServiceProvider sp = services.BuildServiceProvider();

        CacheIdentityEndpointResolver.EnsureResolved(endpoint, sp);

        binding.Contract.Should().BeSameAs(contract);
        metadata.IsResolved.Should().BeTrue();
    }

    private sealed class FixedContract(string name) : ICacheIdentityContract
    {
        public string Name { get; } = name;

        public ValueTask<CacheIdentityMaterial?> BuildAsync(
            CacheIdentityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<CacheIdentityMaterial?>(CacheIdentityMaterial.Empty);
    }
}
