namespace CacheOrchestrator.AdminConsole.Models;

/// <summary>Connectivity of the optional external metrics store.</summary>
public static class MetricsStoreStatusCodes
{
    /// <summary><c>AdminConsole:Metrics</c> disabled or missing BaseUrl.</summary>
    public const string NotConfigured = "NotConfigured";

    /// <summary>Configured but probe failed.</summary>
    public const string Disconnected = "Disconnected";

    /// <summary>Probe succeeded.</summary>
    public const string Connected = "Connected";
}

/// <summary>Result of probing the metrics store.</summary>
public sealed class MetricsStatusDto
{
    /// <summary>One of <see cref="MetricsStoreStatusCodes"/>.</summary>
    public required string Status { get; init; }

    /// <summary>Configured provider name, when any.</summary>
    public string? Provider { get; init; }

    /// <summary>Host portion of BaseUrl (no credentials), for display.</summary>
    public string? Host { get; init; }

    /// <summary>UTC time of this status evaluation.</summary>
    public DateTimeOffset CheckedAtUtc { get; init; }

    /// <summary>Probe latency when a network check ran.</summary>
    public double? LatencyMs { get; init; }

    /// <summary>Error detail when disconnected.</summary>
    public string? Error { get; init; }

    /// <summary>Default range string (e.g. 1h).</summary>
    public string? DefaultRange { get; init; }

    /// <summary>Supported range tokens for the UI.</summary>
    public IReadOnlyList<string> AllowedRanges { get; init; } = MetricsRange.AllowedRanges;
}

/// <summary>Catalog entry for an allowlisted chart panel.</summary>
public sealed class MetricsPanelInfoDto
{
    /// <summary>Stable panel id used in query strings.</summary>
    public required string Id { get; init; }

    /// <summary>Display title.</summary>
    public required string Title { get; init; }

    /// <summary>Short description for tooltips.</summary>
    public required string Description { get; init; }

    /// <summary>Display unit: <c>rate</c>, <c>percent</c>, <c>count</c>, <c>ms</c>.</summary>
    public required string Unit { get; init; }
}

/// <summary>Catalog of panels available when the store is configured.</summary>
public sealed class MetricsCatalogDto
{
    /// <summary>Same status envelope as <see cref="MetricsStatusDto.Status"/>.</summary>
    public required string Status { get; init; }

    /// <summary>Panels (empty when not configured).</summary>
    public required IReadOnlyList<MetricsPanelInfoDto> Panels { get; init; }
}

/// <summary>One numeric sample.</summary>
public sealed class MetricsPointDto
{
    /// <summary>Unix seconds.</summary>
    public long T { get; init; }

    /// <summary>Sample value (NaN omitted by client).</summary>
    public double V { get; init; }
}

/// <summary>One labeled series within a panel.</summary>
public sealed class MetricsSeriesDto
{
    /// <summary>Legend name (usually domain or "cluster").</summary>
    public required string Name { get; init; }

    /// <summary>Prometheus labels retained for debugging.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Time-ordered points.</summary>
    public required IReadOnlyList<MetricsPointDto> Points { get; init; }
}

/// <summary>One chart panel with zero or more series.</summary>
public sealed class MetricsPanelDto
{
    /// <summary>Panel id.</summary>
    public required string Id { get; init; }

    /// <summary>Title.</summary>
    public required string Title { get; init; }

    /// <summary>Plain-language description for UI tooltips (what the chart shows).</summary>
    public string? Description { get; init; }

    /// <summary>Unit for formatting.</summary>
    public required string Unit { get; init; }

    /// <summary>Series for this panel.</summary>
    public required IReadOnlyList<MetricsSeriesDto> Series { get; init; }

    /// <summary>Optional non-fatal note (e.g. empty result).</summary>
    public string? Warning { get; init; }
}

/// <summary>Envelope for range query results.</summary>
public sealed class MetricsSeriesResponseDto
{
    /// <summary><see cref="MetricsStoreStatusCodes"/>.</summary>
    public required string Status { get; init; }

    /// <summary>Resolved range token.</summary>
    public required string Range { get; init; }

    /// <summary>Step used for the query (Prometheus duration, e.g. 30s).</summary>
    public required string Step { get; init; }

    /// <summary>When the query finished.</summary>
    public DateTimeOffset QueriedAtUtc { get; init; }

    /// <summary>Error when status is not Connected (still HTTP 200).</summary>
    public string? Error { get; init; }

    /// <summary>Panels (may be empty).</summary>
    public required IReadOnlyList<MetricsPanelDto> Panels { get; init; }
}

/// <summary>Window summary KPI values for the Metrics page header.</summary>
public sealed class MetricsSummaryDto
{
    /// <summary><see cref="MetricsStoreStatusCodes"/>.</summary>
    public required string Status { get; init; }

    /// <summary>Resolved range.</summary>
    public required string Range { get; init; }

    /// <summary>When evaluated.</summary>
    public DateTimeOffset QueriedAtUtc { get; init; }

    /// <summary>Error when not connected.</summary>
    public string? Error { get; init; }

    /// <summary>Approx request rate (OC outcomes / s), latest sample.</summary>
    public double? RequestRate { get; init; }

    /// <summary>OC hit share 0–1, latest sample.</summary>
    public double? OcHitShare { get; init; }

    /// <summary>FC hit rate 0–1 among FC ops, latest sample.</summary>
    public double? FcHitRate { get; init; }

    /// <summary>Invalidations per second, latest sample.</summary>
    public double? InvalidationRate { get; init; }
}

/// <summary>Parses UI range tokens and maps to step sizes.</summary>
public static class MetricsRange
{
    /// <summary>Ranges offered by the UI and API.</summary>
    public static readonly IReadOnlyList<string> AllowedRanges =
        ["15m", "1h", "6h", "24h", "7d"];

    /// <summary>Resolves a range token or falls back to <paramref name="defaultRange"/>.</summary>
    public static string Normalize(string? range, string defaultRange = "1h")
    {
        string candidate = string.IsNullOrWhiteSpace(range) ? defaultRange : range.Trim();
        foreach (string allowed in AllowedRanges)
        {
            if (string.Equals(allowed, candidate, StringComparison.OrdinalIgnoreCase))
                return allowed;
        }

        return AllowedRanges.Contains(defaultRange, StringComparer.OrdinalIgnoreCase)
            ? defaultRange
            : "1h";
    }

    /// <summary>Maps range token to a Prometheus step duration string.</summary>
    public static string StepFor(string range) => range switch
    {
        "15m" => "15s",
        "1h" => "30s",
        "6h" => "1m",
        "24h" => "2m",
        "7d" => "15m",
        _ => "30s",
    };

    /// <summary>Duration of the range window.</summary>
    public static TimeSpan ToTimeSpan(string range) => range switch
    {
        "15m" => TimeSpan.FromMinutes(15),
        "1h" => TimeSpan.FromHours(1),
        "6h" => TimeSpan.FromHours(6),
        "24h" => TimeSpan.FromHours(24),
        "7d" => TimeSpan.FromDays(7),
        _ => TimeSpan.FromHours(1),
    };
}
