namespace CacheOrchestrator.Configuration;

/// <summary>Controls when HTTP requests bypass Output Cache.</summary>
public enum AuthBypassMode
{
    /// <summary>Never bypass based on authentication.</summary>
    Never = 0,

    /// <summary>Bypass authenticated identities.</summary>
    AuthenticatedIdentityOnly = 1,

    /// <summary>Bypass authenticated identities or requests carrying Authorization.</summary>
    AuthorizationHeaderOnly = 2,

    /// <summary>Bypass authenticated identities or requests carrying Authorization.</summary>
    AuthenticatedOrAuthorization = 3
}
