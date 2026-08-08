using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.UnitTests.Configuration;

public class CacheETagFactoryTests
{
    [Fact]
    public void FromVersion_IsDeterministicWeakEtag()
    {
        var a = CacheETagFactory.FromVersion("v1");
        var b = CacheETagFactory.FromVersion("v1");
        a.ToString().Should().Be(b.ToString());
        a.ToString().Should().StartWith("W/\"");
    }

    [Fact]
    public void FromVersion_DifferentVersions_Differ()
    {
        CacheETagFactory.FromVersion("v1").ToString()
            .Should().NotBe(CacheETagFactory.FromVersion("v2").ToString());
    }

    [Fact]
    public void FromVersionAndResource_SameInputs_Match()
    {
        var a = CacheETagFactory.FromVersionAndResource("abc123", "42");
        var b = CacheETagFactory.FromVersionAndResource("abc123", "42");
        a.ToString().Should().Be(b.ToString());
        a.ToString().Should().Contain("abc123-");
    }

    [Fact]
    public void FromVersionAndResource_DifferentResources_Differ()
    {
        CacheETagFactory.FromVersionAndResource("abc", "1").ToString()
            .Should().NotBe(CacheETagFactory.FromVersionAndResource("abc", "2").ToString());
    }
}
