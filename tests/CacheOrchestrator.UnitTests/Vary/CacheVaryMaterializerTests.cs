using CacheOrchestrator.Configuration;
using CacheOrchestrator.Vary;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

namespace CacheOrchestrator.UnitTests.Vary;

public class CacheVaryMaterializerTests
{
    [Fact]
    public void Build_DefaultOptions_OutputCache_VariesAcceptEncodingWhenPresent()
    {
        var http = CreateHttp(acceptEncoding: "gzip");
        var opts = CreateOptions();

        CacheVaryMaterial material = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.OutputCache);

        material.HeaderNames.Should().Contain(HeaderNames.AcceptEncoding);
        material.Values.Should().NotContainKey("auth-user");
    }

    [Fact]
    public void Build_DefaultOptions_Fusion_VariesEncodingWhenFlagOn()
    {
        var http = CreateHttp(acceptEncoding: "br");
        var opts = CreateOptions(varyEncoding: true);

        CacheVaryMaterial material = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.Fusion);

        material.HeaderNames.Should().Contain(HeaderNames.AcceptEncoding);
    }

    [Fact]
    public void Build_VaryByAccept_IncludesAcceptHeader()
    {
        var http = CreateHttp(accept: "application/json");
        var opts = CreateOptions(varyByAccept: true);

        CacheVaryMaterial material = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.OutputCache);

        material.HeaderNames.Should().Contain(HeaderNames.Accept);
        material.ResponseVaryHeaderNames.Should().Contain(HeaderNames.Accept);
    }

    [Fact]
    public void Build_VaryByQueryKeys_Empty_MeansNoQueryVary()
    {
        var http = CreateHttp(query: new() { ["id"] = "1", ["utm_source"] = "x" });
        var opts = CreateOptions(varyByQueryKeys: []);

        IReadOnlyList<string> keys = CacheVaryMaterializer.ResolveQueryKeys(http.Request.Query, opts);

        keys.Should().BeEmpty();
    }

    [Fact]
    public void Build_VaryByQueryKeys_Allowlist_OnlyListedKeys()
    {
        var http = CreateHttp(query: new() { ["id"] = "1", ["page"] = "2", ["utm_source"] = "x" });
        var opts = CreateOptions(varyByQueryKeys: ["id"]);

        IReadOnlyList<string> keys = CacheVaryMaterializer.ResolveQueryKeys(http.Request.Query, opts);

        keys.Should().Equal("id");
    }

    [Fact]
    public void Build_IgnoreQueryKeys_RemovesExtraKeys()
    {
        var http = CreateHttp(query: new() { ["id"] = "1", ["debug"] = "1" });
        var opts = CreateOptions(ignoreQueryKeys: ["debug"]);

        IReadOnlyList<string> keys = CacheVaryMaterializer.ResolveQueryKeys(http.Request.Query, opts);

        keys.Should().Equal("id");
    }

    [Fact]
    public void Build_SensitiveHeader_IsHashedNotListedInHeaderNames()
    {
        var http = CreateHttp();
        http.Request.Headers.Authorization = "Bearer secret-token";
        var opts = CreateOptions(
            authBypassMode: AuthBypassMode.Never,
            varyByHeaders: ["Authorization"]);

        CacheVaryMaterial material = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.OutputCache);

        material.HeaderNames.Should().NotContain(HeaderNames.Authorization);
        material.Values.Keys.Should().Contain(k => k.StartsWith("hdr:", StringComparison.OrdinalIgnoreCase));
        material.Values["hdr:Authorization"].Should().StartWith("h:");
        string joined = string.Join('|', material.Values.Values);
        joined.Should().NotContain("secret-token");
        material.ResponseVaryHeaderNames.Should().NotContain(HeaderNames.Authorization);
    }

    [Fact]
    public void Build_VaryByCookies_HashesCookieValue()
    {
        var http = CreateHttp();
        http.Features.Set<Microsoft.AspNetCore.Http.Features.IRequestCookiesFeature>(
            new Microsoft.AspNetCore.Http.Features.RequestCookiesFeature(
                new TestCookies(new Dictionary<string, string>
                {
                    ["ab_bucket"] = "variant-a",
                    ["session"] = "raw-secret",
                })));
        var opts = CreateOptions(varyByCookies: ["ab_bucket"]);

        CacheVaryMaterial material = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.Fusion);

        material.Values.Should().ContainKey("cookie:ab_bucket");
        material.Values["cookie:ab_bucket"].Should().StartWith("h:");
        material.Values["cookie:ab_bucket"].Should().NotContain("variant-a");
        material.Values.Should().NotContainKey("cookie:session");
    }

    [Fact]
    public void Build_AuthUser_OnOutputCache_WhenVaryByUser()
    {
        var http = CreateHttp();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "alice")],
            authenticationType: "test"));
        var opts = CreateOptions(authBypassMode: AuthBypassMode.Never, varyByUser: true);

        CacheVaryMaterial material = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.OutputCache);

        material.Values.Should().ContainKey("auth-user");
        material.Values["auth-user"].Should().Be("u:alice");
    }

    [Fact]
    public void Build_AuthUser_NotOnFusion_UnderDefaultBypassMode()
    {
        var http = CreateHttp();
        http.Request.Headers.Authorization = "Bearer x";
        var opts = CreateOptions(authBypassMode: AuthBypassMode.AuthenticatedOrAuthorization, varyByUser: true);

        CacheVaryMaterial material = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.Fusion);

        material.Values.Should().NotContainKey("auth-user");
    }

    [Fact]
    public void Build_AuthUser_OnFusion_WhenAuthBypassNever()
    {
        var http = CreateHttp();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "bob")],
            authenticationType: "test"));
        var opts = CreateOptions(authBypassMode: AuthBypassMode.Never, varyByUser: true);

        CacheVaryMaterial material = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.Fusion);

        material.Values.Should().ContainKey("auth-user");
        material.Values["auth-user"].Should().Be("u:bob");
    }

    [Fact]
    public void Build_VaryByAuthClaims_UsesClaimMaterial()
    {
        var http = CreateHttp();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", "acme"), new Claim(ClaimTypes.Name, "carol")],
            authenticationType: "test"));
        var opts = CreateOptions(
            authBypassMode: AuthBypassMode.Never,
            varyByUser: true,
            varyByAuthClaims: ["tenant_id"]);

        CacheVaryMaterial material = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.OutputCache);

        material.Values["auth-user"].Should().Be("claims:tenant_id=acme");
    }

    [Fact]
    public void Contributor_Order_IsDeterministic()
    {
        var early = new TestContributor(order: 10, key: "a", value: "1");
        var late = new TestContributor(order: 200, key: "b", value: "2");
        var materializer = new CacheVaryMaterializer([late, early]);
        var http = CreateHttp();
        var opts = CreateOptions();

        CacheVaryMaterial material = materializer.Build(http, opts, CacheVarySurface.Fusion);

        material.Values.Should().ContainKey("a").And.ContainKey("b");
        material.Values["a"].Should().Be("1");
        material.Values["b"].Should().Be("2");
    }

    [Fact]
    public void AcceptNormalization_CollapsesToPreferList()
    {
        var http = CreateHttp(accept: "text/html, application/json;q=0.9");
        var opts = CreateOptions(
            varyByAccept: true,
            acceptNormalization: ["application/json", "application/xml"]);

        _ = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.Fusion);

        http.Request.Headers.Accept.ToString().Should().Be("application/json");
    }

    [Fact]
    public void AcceptNormalization_DoesNotMatchJsonSeqAsJson()
    {
        var http = CreateHttp(accept: "application/json-seq");
        var opts = CreateOptions(
            varyByAccept: true,
            acceptNormalization: ["application/json", "application/xml"]);

        _ = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.Fusion);

        http.Request.Headers.Accept.ToString().Should().BeEmpty();
    }

    [Fact]
    public void AcceptNormalization_UsesFirstPreferThatIsPresent()
    {
        var http = CreateHttp(accept: "application/json");
        var opts = CreateOptions(
            varyByAccept: true,
            acceptNormalization: ["application/xml", "application/json"]);

        _ = new CacheVaryMaterializer().Build(http, opts, CacheVarySurface.Fusion);

        http.Request.Headers.Accept.ToString().Should().Be("application/json");
    }

    private static DomainCacheOptions CreateOptions(
        bool varyEncoding = false,
        bool varyByAccept = false,
        bool varyByUser = true,
        AuthBypassMode authBypassMode = AuthBypassMode.AuthenticatedOrAuthorization,
        string[]? varyByQueryKeys = null,
        string[]? ignoreQueryKeys = null,
        string[]? varyByHeaders = null,
        string[]? varyByCookies = null,
        string[]? varyByAuthClaims = null,
        string[]? acceptNormalization = null) => new()
    {
        Domain = "products",
        AuthBypassMode = authBypassMode,
        BypassWhenAuthenticated = authBypassMode != AuthBypassMode.Never,
        VaryOutputCacheByUser = varyByUser,
        FusionCacheVaryOnEncoding = varyEncoding,
        VaryByAccept = varyByAccept,
        VaryByQueryKeys = varyByQueryKeys,
        IgnoreQueryKeys = ignoreQueryKeys,
        VaryByHeaders = varyByHeaders,
        VaryByCookies = varyByCookies,
        VaryByAuthClaims = varyByAuthClaims,
        AcceptNormalizationList = acceptNormalization,
    };

    private static DefaultHttpContext CreateHttp(
        string path = "/api/products",
        Dictionary<string, StringValues>? query = null,
        string? acceptEncoding = null,
        string? accept = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        if (query is not null)
            context.Request.Query = new QueryCollection(query);
        if (acceptEncoding is not null)
            context.Request.Headers.AcceptEncoding = acceptEncoding;
        if (accept is not null)
            context.Request.Headers.Accept = accept;
        return context;
    }

    private sealed class TestContributor : ICacheVaryContributor
    {
        private readonly string _key;
        private readonly string _value;

        public TestContributor(int order, string key, string value)
        {
            Order = order;
            _key = key;
            _value = value;
        }

        public int Order { get; }

        public void Contribute(CacheVaryContext context, ICacheVaryBuilder builder) =>
            builder.AddValue(_key, _value);
    }

    private sealed class TestCookies : IRequestCookieCollection
    {
        private readonly Dictionary<string, string> _map;

        public TestCookies(Dictionary<string, string> map) => _map = map;

        public string? this[string key] => _map.TryGetValue(key, out string? value) ? value : null;

        public int Count => _map.Count;

        public ICollection<string> Keys => _map.Keys;

        public bool ContainsKey(string key) => _map.ContainsKey(key);

        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) =>
            _map.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _map.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
