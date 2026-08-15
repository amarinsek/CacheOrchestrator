namespace CacheOrchestrator.Admin.App.Options;

/// <summary>
/// Optional external metrics storage (Prometheus-compatible) for Admin App time series.
/// Bound from <c>CacheAdmin:Metrics</c>. When disabled or missing URL, the Metrics UI is inactive.
/// Operators typically set only <see cref="Enabled"/>, <see cref="Provider"/>, and <see cref="BaseUrl"/>.
/// </summary>
public sealed class MetricsStoreOptions
{
    /// <summary>When false (default), Metrics features are off — no probe, no series.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Storage kind. Currently only <c>Prometheus</c> (Prometheus / Mimir / VictoriaMetrics / Thanos Query HTTP API).
    /// </summary>
    public string Provider { get; set; } = "Prometheus";

    /// <summary>
    /// Base URL of the metrics HTTP API (scheme + host + port, no trailing slash).
    /// Example: <c>http://prometheus:9090</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Per-request timeout in milliseconds (default 5000).</summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>Default UI/API range when omitted (default <c>1h</c>).</summary>
    public string DefaultRange { get; set; } = "1h";

    /// <summary>
    /// Optional Bearer token for the metrics API (Authorization: Bearer …).
    /// Leave empty for open internal Prometheus.
    /// </summary>
    public string? BearerToken { get; set; }

    /// <summary>
    /// Optional absolute path prefix before Prometheus API paths (e.g. <c>/prometheus</c> behind a reverse proxy).
    /// </summary>
    public string? PathPrefix { get; set; }

    /// <summary>
    /// True when Metrics is enabled and has a BaseUrl (configuration present; connectivity is separate).
    /// </summary>
    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(BaseUrl);
}
