using CacheOrchestrator.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CacheOrchestrator.UnitTests.Utilities;

public class HttpHelperTests
{
    // =========================
    // IsTrackingParameter
    // =========================

    [Theory]
    [InlineData("utm_source")]
    [InlineData("utm_medium")]
    [InlineData("utm_campaign")]
    [InlineData("utm_term")]
    [InlineData("utm_content")]
    [InlineData("UTM_SOURCE")]
    [InlineData("fbclid")]
    [InlineData("gclid")]
    [InlineData("msclkid")]
    [InlineData("ttclid")]
    [InlineData("_ga")]
    [InlineData("_gl")]
    [InlineData("_ga_ABC")]
    public void IsTrackingParameter_KnownTrackingKeys_ReturnsTrue(string key) => HttpHelper.IsTrackingParameter(key).Should().BeTrue();

    [Theory]
    [InlineData("id")]
    [InlineData("page")]
    [InlineData("sort")]
    [InlineData("filter")]
    [InlineData("q")]
    [InlineData("search")]
    [InlineData("category")]
    [InlineData("")]
    [InlineData(null)]
    public void IsTrackingParameter_NormalKeys_ReturnsFalse(string? key) =>
        HttpHelper.IsTrackingParameter(key!).Should().BeFalse();

    // =========================
    // ContainsCacheDirective
    // =========================

    [Fact]
    public void ContainsCacheDirective_WhenNoStorePresent_ReturnsTrue()
    {
        StringValues header = new("private, no-store, max-age=0");
        HttpHelper.ContainsCacheDirective(header, "no-store").Should().BeTrue();
    }

    [Fact]
    public void ContainsCacheDirective_WhenSplitAcrossValues_ReturnsTrue()
    {
        StringValues header = new(["private", "no-store"]);
        HttpHelper.ContainsCacheDirective(header, "no-store").Should().BeTrue();
    }

    [Fact]
    public void ContainsCacheDirective_WhenMissing_ReturnsFalse()
    {
        StringValues header = new("max-age=60, public");
        HttpHelper.ContainsCacheDirective(header, "no-store").Should().BeFalse();
    }

    [Fact]
    public void ContainsCacheDirective_WhenEmpty_ReturnsFalse()
    {
        HttpHelper.ContainsCacheDirective(StringValues.Empty, "no-store").Should().BeFalse();
    }

    // =========================
    // ApplyNoCache
    // =========================

    [Fact]
    public void ApplyNoCache_SetsExpectedHeaders()
    {
        var http = new DefaultHttpContext();

        HttpHelper.ApplyNoCache(http.Response);

        http.Response.Headers.CacheControl.ToString()
            .Should().Be("no-store, no-cache, must-revalidate");
        http.Response.Headers.Pragma.ToString()
            .Should().Be("no-cache");
    }

    // =========================
    // NormalizeAcceptEncoding
    // =========================

    [Fact]
    public void NormalizeAcceptEncoding_WhenMatchingEncoding_SetsToFirstMatch()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.AcceptEncoding = "gzip, deflate, br";

        HttpHelper.NormalizeAcceptEncoding(http, ["br", "gzip"]);

        http.Request.Headers.AcceptEncoding.ToString().Should().Be("br");
    }

    [Fact]
    public void NormalizeAcceptEncoding_WhenNoMatch_ClearsHeader()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.AcceptEncoding = "compress, identity";

        HttpHelper.NormalizeAcceptEncoding(http, ["br", "gzip"]);

        http.Request.Headers.AcceptEncoding.ToString().Should().BeEmpty();
    }

    [Fact]
    public void NormalizeAcceptEncoding_WhenHeaderMissing_DoesNothing()
    {
        var http = new DefaultHttpContext();

        HttpHelper.NormalizeAcceptEncoding(http, ["br", "gzip"]);

        http.Request.Headers.AcceptEncoding.ToString().Should().BeEmpty();
    }

    [Fact]
    public void NormalizeAcceptEncoding_IsCaseInsensitive()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.AcceptEncoding = "GZIP, DEFLATE";

        HttpHelper.NormalizeAcceptEncoding(http, ["gzip"]);

        http.Request.Headers.AcceptEncoding.ToString().Should().Be("gzip");
    }
}