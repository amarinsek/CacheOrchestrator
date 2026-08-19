using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

namespace CacheOrchestrator.UnitTests.Configuration;

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
            BypassWhenAuthenticated = mode != AuthBypassMode.Never,
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
            BypassWhenAuthenticated = true,
            TreatAuthorizationAsAuthSignal = false,
        };

        DomainAuthEvaluator.ShouldBypassForAuth(http, opts).Should().BeFalse();
        DomainAuthEvaluator.HasAuthSignal(http, opts).Should().BeFalse();
    }

    [Fact]
    public void LegacyBypassWhenAuthenticatedFalse_MapsToNever()
    {
        HttpContext http = CreateHttp(authenticated: true, hasAuthorization: true);
        DomainCacheOptions opts = new()
        {
            // AuthBypassMode left at default AuthenticatedOrAuthorization
            BypassWhenAuthenticated = false,
        };

        DomainAuthEvaluator.GetEffectiveAuthBypassMode(opts).Should().Be(AuthBypassMode.Never);
        DomainAuthEvaluator.ShouldBypassForAuth(http, opts).Should().BeFalse();
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
