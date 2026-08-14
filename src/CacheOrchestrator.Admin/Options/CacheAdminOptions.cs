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
    /// After an instance is marked Down, skip further HTTP to it for this many seconds,
    /// then re-probe. Keeps the UI responsive when some targets are offline (default 15).
    /// </summary>
    public int DownReprobeSeconds { get; set; } = 15;

    /// <summary>
    /// Path prefix of the Local Admin API on each instance (no trailing slash).
    /// Default: <c>/cache-admin/local</c>.
    /// </summary>
    public string LocalPathPrefix { get; set; } = "/cache-admin/local";

    /// <summary>
    /// Optional Prometheus-compatible metrics store for time-series UI.
    /// Leave unset (or <c>Enabled: false</c>) when not used.
    /// </summary>
    public MetricsStoreOptions Metrics { get; set; } = new();
}

/// <summary>One target instance in the Admin App instance list.</summary>
public sealed class AdminInstanceOptions
{
    /// <summary>Stable id used in UI and <c>target=instance:{id}</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Base URL of the app instance (scheme + host + port, no trailing slash).</summary>
    public string Url { get; set; } = string.Empty;
}
