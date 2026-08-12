namespace CacheOrchestrator.Admin.App.Options;

/// <summary>
/// Configuration for the Admin App (instance list and fan-out). Bound from <c>CacheAdmin</c>.
/// </summary>
public sealed class CacheAdminOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "CacheAdmin";

    /// <summary>Known application instances exposing Local Admin API.</summary>
    public List<AdminInstanceOptions> Instances { get; set; } = [];

    /// <summary>
    /// Shared API key sent as <c>X-Cache-Admin-Key</c> to each instance.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Per-request timeout when calling a Local Admin API (milliseconds).</summary>
    public int RequestTimeoutMs { get; set; } = 3000;

    /// <summary>Max concurrent HTTP calls during fan-out.</summary>
    public int Parallelism { get; set; } = 8;

    /// <summary>
    /// Path prefix of the Local Admin API on each instance (no trailing slash).
    /// Default: <c>/cache-admin/local</c>.
    /// </summary>
    public string LocalPathPrefix { get; set; } = "/cache-admin/local";
}

/// <summary>One target instance in the Admin App instance list.</summary>
public sealed class AdminInstanceOptions
{
    /// <summary>Stable id used in UI and <c>target=instance:{id}</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Base URL of the app instance (scheme + host + port, no trailing slash).</summary>
    public string Url { get; set; } = string.Empty;
}
