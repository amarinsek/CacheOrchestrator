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

    [Fact]
    public void FromVersionAndResource_SpanParts_MatchConcatenatedKindAndId()
    {
        const string versionHex = "abc123";
        const string entityKind = "products";
        const string resourceId = "42";
        string concatenated = entityKind + ":" + resourceId;

        var fromString = CacheETagFactory.FromVersionAndResource(versionHex, concatenated);
        var fromSpans = CacheETagFactory.FromVersionAndResource(
            versionHex, entityKind.AsSpan(), ":".AsSpan(), resourceId.AsSpan());

        fromSpans.ToString().Should().Be(fromString.ToString());
    }

    [Fact]
    public void FromVersionAndResource_SpanPathAndQuery_MatchConcatenatedPathQuery()
    {
        const string versionHex = "def456";
        const string path = "/api/products/1";
        const string query = "?x=1";
        string concatenated = path + query;

        var fromString = CacheETagFactory.FromVersionAndResource(versionHex, concatenated);
        var fromSpans = CacheETagFactory.FromVersionAndResource(
            versionHex, path.AsSpan(), query.AsSpan());

        fromSpans.ToString().Should().Be(fromString.ToString());
    }

    [Fact]
    public void FromVersionAndResource_EmptyResourceKey_Throws()
    {
        var act = () => CacheETagFactory.FromVersionAndResource("abc123", " ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromVersionAndResource_AllEmptySpans_Throws()
    {
        var act = () => CacheETagFactory.FromVersionAndResource("abc123", default, default, default);
        act.Should().Throw<ArgumentException>().WithParameterName("part1");
    }
}
