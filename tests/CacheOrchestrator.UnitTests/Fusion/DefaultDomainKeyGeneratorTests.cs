using CacheOrchestrator.Configuration;
using CacheOrchestrator.FusionCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.IO.Hashing;
using System.Text;

namespace CacheOrchestrator.UnitTests.Fusion;

public class DefaultDomainKeyGeneratorTests
{
    private readonly DefaultDomainKeyGenerator _sut = new();

    // =========================
    // Determinism & basic behaviour
    // =========================

    [Fact]
    public void Generate_SameRequest_ProducesSameKey()
    {
        var cfg = CreateConfig();
        var http = CreateHttpContext();

        string key1 = _sut.Generate(cfg, http);
        string key2 = _sut.Generate(cfg, http);

        key1.Should().Be(key2);
    }

    [Fact]
    public void Generate_IncludesDomainAndVersion()
    {
        var cfg = CreateConfig(domain: "catalog");
        var http = CreateHttpContext();

        string key = _sut.Generate(cfg, http);

        key.Should().StartWith($"catalog:{cfg.VersionHex}:");
    }

    [Fact]
    public void Generate_WithResourceId_IncludesIdSegmentAndIsStable()
    {
        var cfg = CreateConfig(domain: "products");
        var http = CreateHttpContext();
        http.Items[CacheOrchestratorKeys.EntityKindKey] = "items";
        http.Items[CacheOrchestratorKeys.ResourceIdKey] = "42";

        string key1 = _sut.Generate(cfg, http);
        string key2 = _sut.Generate(cfg, http);

        key1.Should().Be(key2);
        key1.Should().Contain(":id:items:42:");
        key1.Should().StartWith($"products:{cfg.VersionHex}:id:items:42:");
    }

    [Fact]
    public void Generate_DifferentEntityKinds_SameResourceId_ProduceDifferentKeys()
    {
        var cfg = CreateConfig(domain: "store");
        var product = CreateHttpContext();
        product.Items[CacheOrchestratorKeys.EntityKindKey] = "products";
        product.Items[CacheOrchestratorKeys.ResourceIdKey] = "1";
        var asset = CreateHttpContext();
        asset.Items[CacheOrchestratorKeys.EntityKindKey] = "assets";
        asset.Items[CacheOrchestratorKeys.ResourceIdKey] = "1";

        _sut.Generate(cfg, product).Should().NotBe(_sut.Generate(cfg, asset));
        _sut.Generate(cfg, product).Should().Contain(":id:products:1:");
        _sut.Generate(cfg, asset).Should().Contain(":id:assets:1:");
    }

    [Fact]
    public void Generate_ResourceIdWithoutEntityKind_DoesNotUseIdKeyShape()
    {
        var cfg = CreateConfig(domain: "products");
        var http = CreateHttpContext();
        http.Items[CacheOrchestratorKeys.ResourceIdKey] = "42";

        string key = _sut.Generate(cfg, http);

        key.Should().NotContain(":id:42:");
    }

    [Fact]
    public void Generate_DifferentResourceIds_ProduceDifferentKeys()
    {
        var cfg = CreateConfig(domain: "products");
        var http1 = CreateHttpContext();
        http1.Items[CacheOrchestratorKeys.EntityKindKey] = "items";
        http1.Items[CacheOrchestratorKeys.ResourceIdKey] = "1";
        var http2 = CreateHttpContext();
        http2.Items[CacheOrchestratorKeys.EntityKindKey] = "items";
        http2.Items[CacheOrchestratorKeys.ResourceIdKey] = "2";

        _sut.Generate(cfg, http1).Should().NotBe(_sut.Generate(cfg, http2));
    }

    [Fact]
    public void Generate_DifferentDomain_ProducesDifferentKey()
    {
        var http = CreateHttpContext();

        string key1 = _sut.Generate(CreateConfig(domain: "products"), http);
        string key2 = _sut.Generate(CreateConfig(domain: "orders"), http);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_DifferentVersion_ProducesDifferentKey()
    {
        var http = CreateHttpContext();
        var cfg1 = CreateConfig();
        _ = CreateConfig();
        DomainCacheOptions? cfg2 = new DomainCacheOptions
        {
            Domain = cfg1.Domain,
            Version = "2",
            VersionHex = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes("2")).ToString("x16"),
            FusionCacheVaryOnEncoding = false,
            FusionCacheVaryOnPublicAddress = false
        };

        string key1 = _sut.Generate(cfg1, http);
        string key2 = _sut.Generate(cfg2, http);

        key1.Should().NotBe(key2);
    }

    // =========================
    // Path / Route
    // =========================

    [Fact]
    public void Generate_DifferentPath_ProducesDifferentKey()
    {
        var cfg = CreateConfig();

        string key1 = _sut.Generate(cfg, CreateHttpContext(path: "/api/products"));
        string key2 = _sut.Generate(cfg, CreateHttpContext(path: "/api/orders"));

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_EmptyPath_DoesNotThrow()
    {
        var cfg = CreateConfig();
        var http = CreateHttpContext(path: "");

        var act = () => _sut.Generate(cfg, http);

        act.Should().NotThrow();
    }

    // =========================
    // Query parameters
    // =========================

    [Fact]
    public void Generate_DifferentQuery_ProducesDifferentKey()
    {
        var cfg = CreateConfig();

        string key1 = _sut.Generate(cfg, CreateHttpContext(query: new() { ["id"] = "1" }));
        string key2 = _sut.Generate(cfg, CreateHttpContext(query: new() { ["id"] = "2" }));

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_QueryParameterOrder_DoesNotMatter()
    {
        var cfg = CreateConfig();

        var http1 = CreateHttpContext(query: new()
        {
            ["b"] = "2",
            ["a"] = "1"
        });

        var http2 = CreateHttpContext(query: new()
        {
            ["a"] = "1",
            ["b"] = "2"
        });

        string key1 = _sut.Generate(cfg, http1);
        string key2 = _sut.Generate(cfg, http2);

        key1.Should().Be(key2);
    }

    [Fact]
    public void Generate_EmptyQuery_DoesNotThrow()
    {
        var cfg = CreateConfig();
        var http = CreateHttpContext(query: []);

        var act = () => _sut.Generate(cfg, http);

        act.Should().NotThrow();
    }

    // =========================
    // Tracking parameters (must be ignored)
    // =========================

    [Theory]
    [InlineData("utm_source")]
    [InlineData("utm_medium")]
    [InlineData("utm_campaign")]
    [InlineData("utm_term")]
    [InlineData("utm_content")]
    [InlineData("fbclid")]
    [InlineData("gclid")]
    [InlineData("msclkid")]
    [InlineData("ttclid")]
    [InlineData("_ga")]
    [InlineData("_gl")]
    public void Generate_IgnoresTrackingParameter(string trackingParam)
    {
        var cfg = CreateConfig();

        var httpWithTracking = CreateHttpContext(query: new()
        {
            ["id"] = "42",
            [trackingParam] = "something"
        });

        var httpWithoutTracking = CreateHttpContext(query: new()
        {
            ["id"] = "42"
        });

        string key1 = _sut.Generate(cfg, httpWithTracking);
        string key2 = _sut.Generate(cfg, httpWithoutTracking);

        key1.Should().Be(key2);
    }

    [Fact]
    public void Generate_IgnoresMultipleTrackingParameters()
    {
        var cfg = CreateConfig();

        var http1 = CreateHttpContext(query: new() { ["id"] = "42" });
        var http2 = CreateHttpContext(query: new()
        {
            ["id"] = "42",
            ["utm_source"] = "google",
            ["fbclid"] = "abc",
            ["gclid"] = "xyz",
            ["_ga"] = "GA1.2.xxx"
        });

        string key1 = _sut.Generate(cfg, http1);
        string key2 = _sut.Generate(cfg, http2);

        key1.Should().Be(key2);
    }

    // =========================
    // Accept-Encoding
    // =========================

    [Fact]
    public void Generate_VaryOnEncoding_DifferentEncoding_ProducesDifferentKey()
    {
        var cfg = CreateConfig(varyOnEncoding: true);

        string key1 = _sut.Generate(cfg, CreateHttpContext(acceptEncoding: "gzip"));
        string key2 = _sut.Generate(cfg, CreateHttpContext(acceptEncoding: "br"));

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_VaryOnEncodingDisabled_IgnoresEncoding()
    {
        var cfg = CreateConfig(varyOnEncoding: false);

        string key1 = _sut.Generate(cfg, CreateHttpContext(acceptEncoding: "gzip"));
        string key2 = _sut.Generate(cfg, CreateHttpContext(acceptEncoding: "br"));

        key1.Should().Be(key2);
    }

    // =========================
    // Public address (scheme + host)
    // =========================

    [Fact]
    public void Generate_VaryOnPublicAddress_DifferentHost_ProducesDifferentKey()
    {
        var cfg = CreateConfig(varyOnPublicAddress: true);

        string key1 = _sut.Generate(cfg, CreateHttpContext(host: "shop1.example.com"));
        string key2 = _sut.Generate(cfg, CreateHttpContext(host: "shop2.example.com"));

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_VaryOnPublicAddress_DifferentScheme_ProducesDifferentKey()
    {
        var cfg = CreateConfig(varyOnPublicAddress: true);

        string key1 = _sut.Generate(cfg, CreateHttpContext(scheme: "http"));
        string key2 = _sut.Generate(cfg, CreateHttpContext(scheme: "https"));

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_VaryOnPublicAddressDisabled_IgnoresHostAndScheme()
    {
        var cfg = CreateConfig(varyOnPublicAddress: false);

        string key1 = _sut.Generate(cfg, CreateHttpContext(host: "shop1.example.com", scheme: "http"));
        string key2 = _sut.Generate(cfg, CreateHttpContext(host: "shop2.example.com", scheme: "https"));

        key1.Should().Be(key2);
    }

    // =========================
    // Helpers
    // =========================

    private static DomainCacheOptions CreateConfig(
        string domain = "products",
        bool varyOnEncoding = false,
        bool varyOnPublicAddress = false) => new()
        {
            Domain = domain,
            Version = "1",
            VersionHex = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes("1")).ToString("x16"),
            FusionCacheVaryOnEncoding = varyOnEncoding,
            FusionCacheVaryOnPublicAddress = varyOnPublicAddress
        };

    private static DefaultHttpContext CreateHttpContext(
        string path = "/api/products",
        Dictionary<string, StringValues>? query = null,
        string? acceptEncoding = null,
        string scheme = "https",
        string host = "localhost")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);

        if (query is not null)
            context.Request.Query = new QueryCollection(query);

        if (acceptEncoding is not null)
            context.Request.Headers.AcceptEncoding = acceptEncoding;

        return context;
    }
}