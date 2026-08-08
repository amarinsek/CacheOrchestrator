using CacheOrchestrator.OutputCache;

namespace CacheOrchestrator.UnitTests.OutputCaching;

public class CacheDomainAttributeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenDomainIsNullOrWhitespace_Throws(string? domain)
    {
        var act = () => new CacheDomainAttribute(domain!);

        act.Should().Throw<ArgumentException>()
           .WithParameterName("domain");
    }

    [Fact]
    public void Constructor_WhenDomainIsValid_SetsDomainProperty()
    {
        var attr = new CacheDomainAttribute("product-catalog");

        attr.Domain.Should().Be("product-catalog");
    }

    [Fact]
    public void Constructor_DoesNotNormalizeDomain()
    {
        // Normalization is done later by DomainCacheConfigProvider / policy
        var attr = new CacheDomainAttribute("  Product-Catalog  ");

        attr.Domain.Should().Be("  Product-Catalog  ");
    }

    [Fact]
    public void AttributeUsage_IsCorrect()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(CacheDomainAttribute),
            typeof(AttributeUsageAttribute))!;

        usage.Should().NotBeNull();
        usage.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Method);
        usage.AllowMultiple.Should().BeFalse();
        usage.Inherited.Should().BeTrue();
    }
}