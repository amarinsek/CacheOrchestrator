using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Primitives;

namespace CacheOrchestrator.AspNetCore.UnitTests.Configuration;

public class CacheETagFactoryTests
{
    [Fact]
    public void FromVersion_IsDeterministicWeakEtag()
    {
        StringValues a = CacheETagFactory.FromVersion("v1");
        StringValues b = CacheETagFactory.FromVersion("v1");
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
        StringValues a = CacheETagFactory.FromVersionAndResource("abc123", "42");
        StringValues b = CacheETagFactory.FromVersionAndResource("abc123", "42");
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

        StringValues fromString = CacheETagFactory.FromVersionAndResource(versionHex, concatenated);
        StringValues fromSpans = CacheETagFactory.FromVersionAndResource(
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

        StringValues fromString = CacheETagFactory.FromVersionAndResource(versionHex, concatenated);
        StringValues fromSpans = CacheETagFactory.FromVersionAndResource(
            versionHex, path.AsSpan(), query.AsSpan());

        fromSpans.ToString().Should().Be(fromString.ToString());
    }

    [Fact]
    public void FromVersionAndResource_EmptyResourceKey_Throws()
    {
        Func<StringValues> act = () => CacheETagFactory.FromVersionAndResource("abc123", " ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromVersionAndResource_AllEmptySpans_Throws()
    {
        Func<StringValues> act = () => CacheETagFactory.FromVersionAndResource("abc123", default, default, default);
        act.Should().Throw<ArgumentException>().WithParameterName("part1");
    }
}
