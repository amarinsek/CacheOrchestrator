using CacheOrchestrator.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CacheOrchestrator.AspNetCore.UnitTests.Utilities;

public class DomainTemplateCompilerTests
{
    // =========================
    // Validation
    // =========================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetOrAdd_WhenTemplateIsNullOrEmpty_Throws(string? template)
    {
        var act = () => DomainTemplateCompiler.GetOrAdd(template!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetOrAdd_WhenUnclosedBrace_Throws()
    {
        var act = () => DomainTemplateCompiler.GetOrAdd("catalog-{host");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void GetOrAdd_WhenEmptyKey_Throws()
    {
        var act = () => DomainTemplateCompiler.GetOrAdd("catalog-{route:}");
        act.Should().Throw<FormatException>();
    }

    // =========================
    // Literal
    // =========================

    [Fact]
    public void GetOrAdd_LiteralOnly_ReturnsConstant()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("product-catalog");
        var http = new DefaultHttpContext();

        resolver(http).Should().Be("product-catalog");
        resolver(http).Should().Be("product-catalog"); // stable
    }

    // =========================
    // {host}
    // =========================

    [Fact]
    public void GetOrAdd_HostToken_ResolvesHostWithoutPort()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("tenant-{host}");
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("shop.example.com", 443);

        resolver(http).Should().Be("tenant-shop.example.com");
    }

    // =========================
    // {route:name}
    // =========================

    [Fact]
    public void GetOrAdd_RouteToken_ResolvesRouteValue()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("tenant-{route:tenantId}");
        var http = new DefaultHttpContext();
        http.Request.RouteValues["tenantId"] = "acme";

        resolver(http).Should().Be("tenant-acme");
    }

    [Fact]
    public void GetOrAdd_RouteToken_WhenMissing_AppendsNothing()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("tenant-{route:tenantId}");
        var http = new DefaultHttpContext();

        resolver(http).Should().Be("tenant-");
    }

    // =========================
    // {header:name}
    // =========================

    [Fact]
    public void GetOrAdd_HeaderToken_ResolvesHeader()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("api-{header:X-Tenant}");
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Tenant"] = "beta";

        resolver(http).Should().Be("api-beta");
    }

    [Fact]
    public void GetOrAdd_HeaderToken_WhenMissing_AppendsNothing()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("api-{header:X-Tenant}");
        var http = new DefaultHttpContext();

        resolver(http).Should().Be("api-");
    }

    // =========================
    // {query:name}
    // =========================

    [Fact]
    public void GetOrAdd_QueryToken_ResolvesQueryParameter()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("filter-{query:sort}");
        var http = new DefaultHttpContext();
        http.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["sort"] = "price"
        });

        resolver(http).Should().Be("filter-price");
    }

    [Fact]
    public void GetOrAdd_QueryToken_WhenMissing_AppendsNothing()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("filter-{query:sort}");
        var http = new DefaultHttpContext();

        resolver(http).Should().Be("filter-");
    }

    // =========================
    // {custom:key}
    // =========================

    [Fact]
    public void GetOrAdd_CustomToken_UsesProvider()
    {
        var custom = new Dictionary<string, Func<HttpContext, string?>>
        {
            ["region"] = _ => "eu-west"
        };

        var resolver = DomainTemplateCompiler.GetOrAdd("data-{custom:region}", custom);
        var http = new DefaultHttpContext();

        resolver(http).Should().Be("data-eu-west");
    }

    [Fact]
    public void GetOrAdd_CustomToken_WhenProviderMissing_AppendsNothing()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("data2-{custom:missingkey}");
        var http = new DefaultHttpContext();

        resolver(http).Should().Be("data2-");
    }

    // =========================
    // Combined + caching
    // =========================

    [Fact]
    public void GetOrAdd_CombinedTemplate_Works()
    {
        var resolver = DomainTemplateCompiler.GetOrAdd("{host}-tenant-{route:id}-v1");
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("api.example.com");
        http.Request.RouteValues["id"] = "42";

        resolver(http).Should().Be("api.example.com-tenant-42-v1");
    }

    [Fact]
    public void GetOrAdd_SameTemplate_ReturnsCachedDelegate()
    {
        var r1 = DomainTemplateCompiler.GetOrAdd("static-{host}");
        var r2 = DomainTemplateCompiler.GetOrAdd("static-{host}");

        r1.Should().BeSameAs(r2);
    }

    [Fact]
    public void GetOrAdd_DifferentTemplates_ReturnDifferentDelegates()
    {
        var r1 = DomainTemplateCompiler.GetOrAdd("a-{host}");
        var r2 = DomainTemplateCompiler.GetOrAdd("b-{host}");

        r1.Should().NotBeSameAs(r2);
    }

    // =========================
    // customProviders must not poison the shared template cache
    // =========================

    [Fact]
    public void GetOrAdd_CustomProviders_DoNotPoisonSharedTemplateCache()
    {
        // Unique template so we do not interact with other tests' cache entries.
        const string template = "poison-check-{custom:region}-v1";

        var customA = new Dictionary<string, Func<HttpContext, string?>>
        {
            ["region"] = _ => "eu"
        };
        var customB = new Dictionary<string, Func<HttpContext, string?>>
        {
            ["region"] = _ => "us"
        };

        var http = new DefaultHttpContext();

        // First compile with custom providers must NOT be stored under the template-only key.
        var withA = DomainTemplateCompiler.GetOrAdd(template, customA);
        withA(http).Should().Be("poison-check-eu-v1");

        // Later compile without providers must not reuse customA.
        var without = DomainTemplateCompiler.GetOrAdd(template);
        without(http).Should().Be("poison-check--v1");

        // Different provider maps must each bind their own value.
        var withB = DomainTemplateCompiler.GetOrAdd(template, customB);
        withB(http).Should().Be("poison-check-us-v1");
        withA(http).Should().Be("poison-check-eu-v1");
    }

    [Fact]
    public void GetOrAdd_CustomProviders_AfterCachedPlain_StillUsesProviders()
    {
        const string template = "plain-first-{custom:zone}-v1";
        var http = new DefaultHttpContext();

        // Plain compile first (goes into shared cache).
        var plain = DomainTemplateCompiler.GetOrAdd(template);
        plain(http).Should().Be("plain-first--v1");

        var custom = new Dictionary<string, Func<HttpContext, string?>>
        {
            ["zone"] = _ => "zone-a"
        };

        // Custom compile after plain must still capture providers.
        var withCustom = DomainTemplateCompiler.GetOrAdd(template, custom);
        withCustom(http).Should().Be("plain-first-zone-a-v1");

        // Plain entry remains shared and unchanged.
        var plainAgain = DomainTemplateCompiler.GetOrAdd(template);
        plainAgain.Should().BeSameAs(plain);
        plainAgain(http).Should().Be("plain-first--v1");
    }

    [Fact]
    public void GetOrAdd_EmptyCustomProviders_UsesSharedCache()
    {
        const string template = "empty-custom-{host}";
        var empty = new Dictionary<string, Func<HttpContext, string?>>();

        var r1 = DomainTemplateCompiler.GetOrAdd(template, empty);
        var r2 = DomainTemplateCompiler.GetOrAdd(template);

        r1.Should().BeSameAs(r2);
    }
}