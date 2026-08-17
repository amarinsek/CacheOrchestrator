using CacheOrchestrator.AdminConsole.Models;

namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>
/// Resolved query window for Prometheus (relative range token or absolute from/to).
/// </summary>
public sealed record MetricsWindow(
    DateTimeOffset Start,
    DateTimeOffset End,
    string Step,
    string RangeLabel,
    bool IsAbsolute)
{
    /// <summary>Duration of the window.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>
    /// Prometheus range-vector duration for <c>increase</c>/<c>rate</c> over the full window
    /// (e.g. <c>3600s</c>).
    /// </summary>
    public string PromRangeDuration
    {
        get
        {
            long sec = Math.Max(1, (long)Math.Ceiling(Duration.TotalSeconds));
            return sec + "s";
        }
    }

    /// <summary>
    /// Relative token (15m…7d) or <c>custom</c> for absolute from/to.
    /// Absolute wins when both from and to parse and to &gt; from.
    /// </summary>
    public static MetricsWindow Resolve(string? range, string? from, string? to, DateTimeOffset now)
    {
        DateTimeOffset? fromUtc = TryParseTime(from);
        DateTimeOffset? toUtc = TryParseTime(to);
        if (fromUtc is DateTimeOffset f && toUtc is DateTimeOffset t && t > f)
        {
            if (t - f > TimeSpan.FromDays(31))
                f = t - TimeSpan.FromDays(31);
            return new MetricsWindow(f, t, MetricsRange.StepForDuration(t - f), "custom", IsAbsolute: true);
        }

        string resolved = MetricsRange.Normalize(range, "1h");
        DateTimeOffset end = now;
        DateTimeOffset start = end - MetricsRange.ToTimeSpan(resolved);
        return new MetricsWindow(start, end, MetricsRange.StepFor(resolved), resolved, IsAbsolute: false);
    }

    /// <summary>Scrape / series label when instance id is missing on samples.</summary>
    public const string UndefinedInstanceId = "undefined";

    private static DateTimeOffset? TryParseTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        string s = raw.Trim();
        if (long.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long unix))
        {
            if (unix > 1_000_000_000_000L)
                return DateTimeOffset.FromUnixTimeMilliseconds(unix);
            if (unix > 1_000_000_000L)
                return DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset dto))
            return dto;
        if (DateTimeOffset.TryParse(s, out dto))
            return dto.ToUniversalTime();
        return null;
    }
}
