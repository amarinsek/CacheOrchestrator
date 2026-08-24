namespace CacheOrchestrator.Configuration;

/// <summary>
/// Controls when Output Cache (and optionally the data cache) auto-bypasses for auth traffic.
/// </summary>
public enum AuthBypassMode
{
    /// <summary>Never auto-bypass for auth; the app opts into caching and must configure vary carefully.</summary>
    Never = 0,

    /// <summary>Bypass only when <c>User.Identity.IsAuthenticated</c> is true.</summary>
    AuthenticatedIdentityOnly = 1,

    /// <summary>Bypass only when the request has an <c>Authorization</c> header.</summary>
    AuthorizationHeaderOnly = 2,

    /// <summary>
    /// Bypass when the user is authenticated <em>or</em> an <c>Authorization</c> header is present
    /// (subject to <see cref="DomainCacheOptions.TreatAuthorizationAsAuthSignal"/>).
    /// This matches the historical <c>BypassWhenAuthenticated = true</c> behaviour.
    /// </summary>
    AuthenticatedOrAuthorization = 3,
}
