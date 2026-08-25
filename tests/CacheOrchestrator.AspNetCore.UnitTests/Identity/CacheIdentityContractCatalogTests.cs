using CacheOrchestrator.Identity;

namespace CacheOrchestrator.AspNetCore.UnitTests.Identity;

public class CacheIdentityContractCatalogTests
{
    [Fact]
    public void Constructor_DuplicateNames_Throws()
    {
        ICacheIdentityContract a = new Named("search-v1");
        ICacheIdentityContract b = new Named("search-v1");

        Action act = () => _ = new CacheIdentityContractCatalog([a, b]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*search-v1*");
    }

    [Fact]
    public void Constructor_ReservedUrlName_Throws()
    {
        ICacheIdentityContract bad = new Named(CacheIdentities.Url);

        Action act = () => _ = new CacheIdentityContractCatalog([bad]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{CacheIdentities.Url}*");
    }

    [Fact]
    public void TryGet_ReturnsRegisteredContract()
    {
        ICacheIdentityContract contract = new Named("search-v1");
        CacheIdentityContractCatalog catalog = new([contract]);

        catalog.TryGet("SEARCH-V1", out ICacheIdentityContract found).Should().BeTrue();
        found.Should().BeSameAs(contract);
    }

    private sealed class Named(string name) : ICacheIdentityContract
    {
        public string Name { get; } = name;

        public ValueTask<CacheIdentityMaterial?> BuildAsync(
            CacheIdentityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<CacheIdentityMaterial?>(CacheIdentityMaterial.Empty);
    }
}
