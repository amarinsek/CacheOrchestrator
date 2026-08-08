using CacheOrchestrator.Configuration;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using System.IO.Hashing;
using System.Text;

namespace CacheOrchestrator.UnitTests.OutputCaching;

public class DomainOutputCachePolicyTests
{
    // =========================
    // Constructor
    // =========================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenDomainIsNullOrWhitespace_Throws(string? domain)
    {
        var act = () => new DomainOutputCachePolicy(domain!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenDomainResolverIsNull_Throws()
    {
        var act = () => new DomainOutputCachePolicy((Func<HttpContext, string>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // =========================
    // CacheRequestAsync � early exits
    // =========================

    [Fact]
    public async Task CacheRequestAsync_WhenDomainIsEmpty_DoesNotEnableCaching()
    {
        var policy = new DomainOutputCachePolicy(_ => string.Empty);
        var (context, _) = CreateContext();

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task CacheRequestAsync_WhenMethodIsNotGetOrHead_DoesNotEnableCaching(string method)
    {
        var policy = new DomainOutputCachePolicy("products");

        var (context, _) = CreateContext(method: method);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
    }

    [Fact]
    public async Task CacheRequestAsync_WhenUserIsAuthenticated_DoesNotEnableCaching()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext();

        var identity = new System.Security.Claims.ClaimsIdentity(authenticationType: "test");
        http.User = new System.Security.Claims.ClaimsPrincipal(identity);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
    }

    [Fact]
    public async Task CacheRequestAsync_WhenAuthorizationHeaderPresent_DoesNotEnableCaching()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext();
        http.Request.Headers.Authorization = "Bearer token";

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
    }

    [Fact]
    public async Task CacheRequestAsync_WhenAuthenticated_AndBypassDisabled_EnablesCachingAndVariesByUser()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext(bypassWhenAuthenticated: false, varyOutputCacheByUser: true);

        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "alice")],
            authenticationType: "test");
        http.User = new System.Security.Claims.ClaimsPrincipal(identity);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeTrue();
        context.CacheVaryByRules.VaryByValues["auth-user"].Should().Be("u:alice");
    }

    [Fact]
    public async Task CacheRequestAsync_WhenAuthorizationOnly_BypassDisabled_VariesByAuthHash()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext(bypassWhenAuthenticated: false, varyOutputCacheByUser: true);
        http.Request.Headers.Authorization = "Bearer secret-token";

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeTrue();
        context.CacheVaryByRules.VaryByValues["auth-user"].ToString().Should().StartWith("ah:");
    }

    [Fact]
    public async Task CacheRequestAsync_WhenAuthenticated_BypassDisabled_VaryByUserFalse_DoesNotAddUserVary()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext(bypassWhenAuthenticated: false, varyOutputCacheByUser: false);
        http.Request.Headers.Authorization = "Bearer map-api-key";

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeTrue();
        context.CacheVaryByRules.VaryByValues.ContainsKey("auth-user").Should().BeFalse();
    }

    [Fact]
    public async Task CacheRequestAsync_WhenOutputCacheDisabled_DoesNotEnableCaching()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, _) = CreateContext(outputCacheEnabled: false);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
    }

    [Fact]
    public async Task CacheRequestAsync_WhenRequestHasNoStore_DoesNotEnableCaching()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext();
        http.Request.Headers.CacheControl = "no-store";

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeFalse();
    }

    // =========================
    // CacheRequestAsync � happy path
    // =========================

    [Fact]
    public async Task CacheRequestAsync_WhenValidGetRequest_EnablesCaching()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, _) = CreateContext();

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.EnableOutputCaching.Should().BeTrue();
        context.AllowCacheLookup.Should().BeTrue();
        context.AllowCacheStorage.Should().BeTrue();
        context.AllowLocking.Should().BeTrue();
    }

    [Fact]
    public async Task CacheRequestAsync_AddsDomainTag()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, _) = CreateContext();

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.Tags.Should().Contain("domain:products");
    }

    [Fact]
    public async Task CacheRequestAsync_SetsExpirationFromConfig()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, _) = CreateContext(outputTtlSeconds: 120);

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.ResponseExpirationTimeSpan.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task CacheRequestAsync_SetsETag()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext(version: "v1");

        await policy.CacheRequestAsync(context, CancellationToken.None);

        ulong versionHash = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes("v1"));
        http.Response.Headers.ETag.ToString().Should().Be($"W/\"{versionHash:x16}\"");
    }

    [Fact]
    public async Task CacheRequestAsync_IgnoresTrackingQueryParametersInVary()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext();
        http.Request.QueryString = new QueryString("?id=1&utm_source=google&fbclid=abc");

        // Re-create query collection properly
        http.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["id"] = "1",
            ["utm_source"] = "google",
            ["fbclid"] = "abc"
        });

        await policy.CacheRequestAsync(context, CancellationToken.None);

        context.CacheVaryByRules.QueryKeys.Should().Contain("id");
        context.CacheVaryByRules.QueryKeys.Should().NotContain("utm_source");
        context.CacheVaryByRules.QueryKeys.Should().NotContain("fbclid");
    }

    // =========================
    // ServeResponseAsync
    // =========================

    [Fact]
    public async Task ServeResponseAsync_WhenStatusNotCacheable_DisablesStorage()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext();
        http.Response.StatusCode = 500;

        // Simulate that EnsureConfig already ran
        var cfg = CreateEffectiveConfig();
        http.Items[CacheOrchestratorKeys.DomainOptionsKey] = cfg;
        context.AllowCacheStorage = true;

        await policy.ServeResponseAsync(context, CancellationToken.None);

        context.AllowCacheStorage.Should().BeFalse();
    }

    [Fact]
    public async Task ServeResponseAsync_WhenSetCookiePresent_DisablesStorage()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext();
        http.Response.StatusCode = 200;
        http.Response.Headers.SetCookie = "session=abc";

        var cfg = CreateEffectiveConfig();
        http.Items[CacheOrchestratorKeys.DomainOptionsKey] = cfg;
        context.AllowCacheStorage = true;

        await policy.ServeResponseAsync(context, CancellationToken.None);

        context.AllowCacheStorage.Should().BeFalse();
    }

    [Fact]
    public async Task ServeResponseAsync_WhenStatus200AndNoSensitiveHeaders_KeepsStorageEnabled()
    {
        var policy = new DomainOutputCachePolicy("products");
        var (context, http) = CreateContext();
        http.Response.StatusCode = 200;

        var cfg = CreateEffectiveConfig();
        http.Items[CacheOrchestratorKeys.DomainOptionsKey] = cfg;
        context.AllowCacheStorage = true;

        await policy.ServeResponseAsync(context, CancellationToken.None);

        context.AllowCacheStorage.Should().BeTrue();
    }

    // =========================
    // Helpers
    // =========================

    private static (OutputCacheContext context, DefaultHttpContext http) CreateContext(
        string method = "GET",
        bool outputCacheEnabled = true,
        int outputTtlSeconds = 60,
        string? version = null,
        bool bypassWhenAuthenticated = true,
        bool varyOutputCacheByUser = true)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = "/api/products";

        var cfg = CreateEffectiveConfig(
            outputCacheEnabled,
            outputTtlSeconds,
            version,
            bypassWhenAuthenticated,
            varyOutputCacheByUser);

        var domainConfig = Substitute.For<IDomainCacheOptionsProvider>();
        domainConfig.EnsureDomainOptions(http, Arg.Any<string>()).Returns(cfg);

        var services = new ServiceCollection();
        services.AddSingleton(domainConfig);
        services.AddSingleton(typeof(ILogger<DomainOutputCachePolicy>), NullLogger<DomainOutputCachePolicy>.Instance);
        http.RequestServices = services.BuildServiceProvider();

        var context = new OutputCacheContext
        {
            HttpContext = http
        };

        return (context, http);
    }

    private static DomainCacheOptions CreateEffectiveConfig(
        bool outputCacheEnabled = true,
        int outputTtlSeconds = 60,
        string? version = null,
        bool bypassWhenAuthenticated = true,
        bool varyOutputCacheByUser = true) => new()
        {
            Domain = "products",
            OutputCacheEnabled = outputCacheEnabled,
            BypassWhenAuthenticated = bypassWhenAuthenticated,
            VaryOutputCacheByUser = varyOutputCacheByUser,
            OutputTtl = TimeSpan.FromSeconds(outputTtlSeconds),
            Version = version ?? "1",
            VersionHex = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(version ?? "1")).ToString("x16"),
            ETag = new StringValues($"W/\"{XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(version ?? "1")):x16}\""),
            CacheableStatusCodes = [200],
            ClientCacheability = ClientCacheability.Public,
            ClientTtlSeconds = 60,
            ClientTtlMinSeconds = 60,
            ScheduledUpdateUtc = null,
            ClientMustRevalidateNearUpdate = false,
            OutputCacheNamespace = "test-oc",
            EncodingNormalizationList = null
        };
}