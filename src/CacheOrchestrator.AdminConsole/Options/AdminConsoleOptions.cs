namespace CacheOrchestrator.AdminConsole.Options;

/// <summary>
/// Configuration for the Admin Console App (instance list and fan-out). Bound from <c>AdminConsole</c>.
/// </summary>
/// <remarks>
/// Fan-out and Metrics HTTP clients resolve this via <c>IOptions&lt;AdminConsoleOptions&gt;</c>
/// (snapshot at construction). Changing <see cref="Instances"/>, timeouts, <see cref="ApiKey"/>,
/// or <see cref="Metrics"/> at runtime requires a process restart. Hint packs use
/// <c>IOptionsMonitor</c> and support reload without restart.
/// </remarks>
public sealed class AdminConsoleOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AdminConsole";

    /// <summary>
    /// Known application instances exposing Admin API.
    /// Changes require process restart (see type remarks).
    /// </summary>
    public List<AdminInstanceOptions> Instances { get; set; } = [];

    /// <summary>
    /// Shared API key sent as <c>X-Cache-Admin-Key</c> to each instance.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Per-request timeout when calling a Admin API (milliseconds).</summary>
    public int RequestTimeoutMs { get; set; } = 3000;

    /// <summary>Max concurrent HTTP calls during fan-out.</summary>
    public int Parallelism { get; set; } = 8;

    /// <summary>
    /// After an instance is marked Down, skip further HTTP to it for this many seconds,
    /// then re-probe. Keeps the UI responsive when some targets are offline (default 15).
    /// </summary>
    public int DownReprobeSeconds { get; set; } = 15;

    /// <summary>
    /// Path prefix of the Admin API on each instance (no trailing slash).
    /// Default: <c>/cache-admin/local</c>.
    /// </summary>
    public string AdminApiPathPrefix { get; set; } = "/cache-admin/local";

    /// <summary>
    /// Optional Prometheus-compatible metrics store for time-series UI.
    /// Leave unset (or <c>Enabled: false</c>) when not used.
    /// </summary>
    public MetricsStoreOptions Metrics { get; set; } = new();

    /// <summary>
    /// Operator recommendation rules (built-in + optional declarative JSON files).
    /// </summary>
    public HintOptions Hints { get; set; } = new();
}

/// <summary>One target instance in the Admin Console App instance list.</summary>
public sealed class AdminInstanceOptions
{
    /// <summary>Stable id used in UI and API responses.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Base URL of the app instance (scheme + host + port, no trailing slash).</summary>
    public string Url { get; set; } = string.Empty;
}
