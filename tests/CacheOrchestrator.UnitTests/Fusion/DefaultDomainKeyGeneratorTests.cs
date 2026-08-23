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
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "items", ResourceId = "42" });

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
        product.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "products", ResourceId = "1" });
        var asset = CreateHttpContext();
        asset.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "assets", ResourceId = "1" });

        _sut.Generate(cfg, product).Should().NotBe(_sut.Generate(cfg, asset));
        _sut.Generate(cfg, product).Should().Contain(":id:products:1:");
        _sut.Generate(cfg, asset).Should().Contain(":id:assets:1:");
    }

    [Fact]
    public void Generate_ResourceIdWithoutEntityKind_DoesNotUseIdKeyShape()
    {
        var cfg = CreateConfig(domain: "products");
        var http = CreateHttpContext();
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { ResourceId = "42" });

        string key = _sut.Generate(cfg, http);

        key.Should().NotContain(":id:42:");
    }

    [Fact]
    public void Generate_DifferentResourceIds_ProduceDifferentKeys()
    {
        var cfg = CreateConfig(domain: "products");
        var http1 = CreateHttpContext();
        http1.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "items", ResourceId = "1" });
        var http2 = CreateHttpContext();
        http2.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "items", ResourceId = "2" });

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

    [Fact]
    public void Generate_DoesNotTreatGameQueryAsTracking()
    {
        var cfg = CreateConfig();
        string withGame = _sut.Generate(cfg, CreateHttpContext(query: new() { ["id"] = "42", ["_game"] = "1" }));
        string without = _sut.Generate(cfg, CreateHttpContext(query: new() { ["id"] = "42" }));

        withGame.Should().NotBe(without);
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

    [Fact]
    public void Generate_WithAcceptNormalization_DoesNotLeaveMutatedRequestHeaders()
    {
        var cfg = new DomainCacheOptions
        {
            Domain = "products",
            Version = "1",
            VersionHex = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes("1")).ToString("x16"),
            FusionCacheVaryOnEncoding = false,
            FusionCacheVaryOnPublicAddress = false,
            VaryByAccept = true,
            AcceptNormalizationList = ["application/json", "application/xml"],
        };

        const string original = "text/html, application/json;q=0.9";
        var http = CreateHttpContext(accept: original);

        _ = _sut.Generate(cfg, http);

        http.Request.Headers.Accept.ToString().Should().Be(original);
    }

    [Fact]
    public void Generate_AcceptNormalization_SamePreferMatch_ProducesSameKey()
    {
        var cfg = new DomainCacheOptions
        {
            Domain = "products",
            Version = "1",
            VersionHex = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes("1")).ToString("x16"),
            FusionCacheVaryOnEncoding = false,
            FusionCacheVaryOnPublicAddress = false,
            VaryByAccept = true,
            AcceptNormalizationList = ["application/json", "application/xml"],
        };

        string key1 = _sut.Generate(cfg, CreateHttpContext(accept: "text/html, application/json;q=0.9"));
        string key2 = _sut.Generate(cfg, CreateHttpContext(accept: "application/json"));

        key1.Should().Be(key2);
    }

    [Fact]
    public void Generate_AfterEntityItemsCleared_UsesUrlKeyShape()
    {
        var cfg = CreateConfig(domain: "products");
        var http = CreateHttpContext();
        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature { EntityKind = "items", ResourceId = "42" });
        string entityKey = _sut.Generate(cfg, http);

        http.Features.Set<ICacheOrchestratorFeature>(new CacheOrchestratorFeature());
        string urlKey = _sut.Generate(cfg, http);

        entityKey.Should().Contain(":id:items:42:");
        urlKey.Should().NotContain(":id:");
    }

    [Fact]
    public void Generate_VaryByAccept_DifferentAccept_ProducesDifferentKey()
    {
        var cfg = CreateConfig();
        cfg = new DomainCacheOptions
        {
            Domain = cfg.Domain,
            Version = cfg.Version,
            VersionHex = cfg.VersionHex,
            FusionCacheVaryOnEncoding = false,
            FusionCacheVaryOnPublicAddress = false,
            VaryByAccept = true,
        };

        string key1 = _sut.Generate(cfg, CreateHttpContext(accept: "application/json"));
        string key2 = _sut.Generate(cfg, CreateHttpContext(accept: "application/xml"));

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_VaryByQueryKeysAllowlist_IgnoresOtherKeys()
    {
        var cfg = new DomainCacheOptions
        {
            Domain = "products",
            Version = "1",
            VersionHex = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes("1")).ToString("x16"),
            FusionCacheVaryOnEncoding = false,
            FusionCacheVaryOnPublicAddress = false,
            VaryByQueryKeys = ["id"],
        };

        string key1 = _sut.Generate(cfg, CreateHttpContext(query: new() { ["id"] = "1", ["page"] = "1" }));
        string key2 = _sut.Generate(cfg, CreateHttpContext(query: new() { ["id"] = "1", ["page"] = "99" }));

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
        string? accept = null,
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

        if (accept is not null)
            context.Request.Headers.Accept = accept;

        return context;
    }
}