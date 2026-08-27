using CacheOrchestrator.EFCore;

namespace CacheOrchestrator.EFCore.Invalidation.UnitTests;

public class CacheEntityAttributeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenDomainIsNullOrWhitespace_Throws(string? domain)
    {
        Func<CacheEntityAttribute> act = () => new CacheEntityAttribute(domain!, "products");
        act.Should().Throw<ArgumentException>().WithParameterName("domain");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenEntityKindIsNullOrWhitespace_Throws(string? kind)
    {
        Func<CacheEntityAttribute> act = () => new CacheEntityAttribute("store", kind!);
        act.Should().Throw<ArgumentException>().WithParameterName("entityKind");
    }

    [Fact]
    public void Constructor_TrimsDomainAndKind()
    {
        CacheEntityAttribute attr = new("  store  ", "  products  ");
        attr.Domain.Should().Be("store");
        attr.EntityKind.Should().Be("products");
    }
}
