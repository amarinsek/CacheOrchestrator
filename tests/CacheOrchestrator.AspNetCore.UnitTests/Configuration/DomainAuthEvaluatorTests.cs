using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

namespace CacheOrchestrator.AspNetCore.UnitTests.Configuration;

public class DomainAuthEvaluatorTests
{
    [Theory]
    [InlineData(AuthBypassMode.Never, true, true, false)]
    [InlineData(AuthBypassMode.AuthenticatedIdentityOnly, true, false, true)]
    [InlineData(AuthBypassMode.AuthenticatedIdentityOnly, false, true, false)]
    [InlineData(AuthBypassMode.AuthorizationHeaderOnly, false, true, true)]
    [InlineData(AuthBypassMode.AuthorizationHeaderOnly, true, false, false)]
    [InlineData(AuthBypassMode.AuthenticatedOrAuthorization, true, false, true)]
    [InlineData(AuthBypassMode.AuthenticatedOrAuthorization, false, true, true)]
    [InlineData(AuthBypassMode.AuthenticatedOrAuthorization, false, false, false)]
    public void ShouldBypassForAuth_RespectsMode(
        AuthBypassMode mode,
        bool authenticated,
        bool hasAuthorization,
        bool expectedBypass)
    {
        HttpContext http = CreateHttp(authenticated, hasAuthorization);
        DomainCacheOptions opts = new()
        {
            AuthBypassMode = mode,
            TreatAuthorizationAsAuthSignal = true,
        };

        DomainAuthEvaluator.ShouldBypassForAuth(http, opts).Should().Be(expectedBypass);
    }

    [Fact]
    public void TreatAuthorizationAsAuthSignal_False_IgnoresAuthorizationHeaderForOrMode()
    {
        HttpContext http = CreateHttp(authenticated: false, hasAuthorization: true);
        DomainCacheOptions opts = new()
        {
            AuthBypassMode = AuthBypassMode.AuthenticatedOrAuthorization,
            TreatAuthorizationAsAuthSignal = false,
        };

        DomainAuthEvaluator.ShouldBypassForAuth(http, opts).Should().BeFalse();
        DomainAuthEvaluator.HasAuthSignal(http, opts).Should().BeFalse();
    }

    [Fact]
    public void GetEffectiveAuthBypassMode_ReturnsConfiguredMode()
    {
        DomainCacheOptions opts = new() { AuthBypassMode = AuthBypassMode.Never };

        DomainAuthEvaluator.GetEffectiveAuthBypassMode(opts).Should().Be(AuthBypassMode.Never);
    }

    [Fact]
    public void ResolveAuthenticatedVaryKey_UsesNamePrefix()
    {
        HttpContext http = CreateHttp(authenticated: true, hasAuthorization: false);
        DomainCacheOptions opts = new() { AuthBypassMode = AuthBypassMode.Never };

        DomainAuthEvaluator.ResolveAuthenticatedVaryKey(http, opts).Should().Be("u:user");
    }

    [Fact]
    public void ResolveAuthenticatedVaryKey_UsesSortedClaims()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tenant_id", "acme"), new Claim("role", "admin"), new Claim(ClaimTypes.Name, "carol")],
                authenticationType: "test"))
        };
        DomainCacheOptions opts = new()
        {
            AuthBypassMode = AuthBypassMode.Never,
            VaryByAuthClaims = ["tenant_id", "role"],
        };

        DomainAuthEvaluator.ResolveAuthenticatedVaryKey(http, opts)
            .Should().Be("claims:role=admin;tenant_id=acme");
    }

    [Fact]
    public void ResolveAuthenticatedVaryKey_HashesAuthorization_AndDoesNotLeakToken()
    {
        HttpContext http = CreateHttp(authenticated: false, hasAuthorization: true);
        DomainCacheOptions opts = new()
        {
            AuthBypassMode = AuthBypassMode.Never,
            AuthVaryIncludeAuthorizationHash = true,
        };

        string key = DomainAuthEvaluator.ResolveAuthenticatedVaryKey(http, opts);
        key.Should().StartWith("ah:");
        key.Should().NotContain("Bearer");
        key.Should().NotContain("token");
        key.Length.Should().Be("ah:".Length + 16);
    }

    [Fact]
    public void ResolveAuthenticatedVaryKey_WhenHashDisabled_ReturnsAuthSentinel()
    {
        HttpContext http = CreateHttp(authenticated: false, hasAuthorization: true);
        DomainCacheOptions opts = new()
        {
            AuthBypassMode = AuthBypassMode.Never,
            AuthVaryIncludeAuthorizationHash = false,
        };

        DomainAuthEvaluator.ResolveAuthenticatedVaryKey(http, opts).Should().Be("auth");
    }

    [Fact]
    public void ResolveAuthenticatedVaryKey_UsesSubWhenNameMissing()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "user-42")],
                authenticationType: "test"))
        };
        DomainCacheOptions opts = new() { AuthBypassMode = AuthBypassMode.Never };

        DomainAuthEvaluator.ResolveAuthenticatedVaryKey(http, opts).Should().Be("id:user-42");
    }

    [Fact]
    public void ResolveAuthenticatedVaryKey_UsesNameIdentifierWhenNameAndSubMissing()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "nid-7")],
                authenticationType: "test"))
        };
        DomainCacheOptions opts = new() { AuthBypassMode = AuthBypassMode.Never };

        DomainAuthEvaluator.ResolveAuthenticatedVaryKey(http, opts).Should().Be("id:nid-7");
    }

    [Fact]
    public void ResolveAuthenticatedVaryKey_WhenAnonymous_ReturnsAuthSentinel()
    {
        HttpContext http = CreateHttp(authenticated: false, hasAuthorization: false);
        DomainCacheOptions opts = new() { AuthBypassMode = AuthBypassMode.Never };

        DomainAuthEvaluator.ResolveAuthenticatedVaryKey(http, opts).Should().Be("auth");
    }

    private static DefaultHttpContext CreateHttp(bool authenticated, bool hasAuthorization)
    {
        var http = new DefaultHttpContext();
        if (authenticated)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "user")],
                authenticationType: "test"));
        }

        if (hasAuthorization)
            http.Request.Headers[HeaderNames.Authorization] = "Bearer token";

        return http;
    }
}
