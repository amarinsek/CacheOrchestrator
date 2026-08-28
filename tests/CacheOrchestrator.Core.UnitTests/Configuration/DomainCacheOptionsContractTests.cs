using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Core.UnitTests.Configuration;

public class DomainCacheOptionsContractTests
{
    [Fact]
    public void PublicProperties_AreHttpFreeCorePolicy()
    {
        string[] properties = typeof(DomainCacheOptions)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        properties.Should().Equal(
            "DataCacheEnabled",
            "DataCacheInstanceName",
            "DataCacheNamespace",
            "DataCacheTtl",
            "Domain",
            "Version",
            "VersionHex");
    }
}
