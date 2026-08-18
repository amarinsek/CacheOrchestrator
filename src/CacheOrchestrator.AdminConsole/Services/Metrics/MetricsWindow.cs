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
    /// Start/end are snapped down onto the <see cref="Step"/> grid so auto-refresh
    /// does not slide <c>query_range</c> timestamps every few seconds (Grafana-style).
    /// </summary>
    public static MetricsWindow Resolve(string? range, string? from, string? to, DateTimeOffset now)
    {
        DateTimeOffset? fromUtc = TryParseTime(from);
        DateTimeOffset? toUtc = TryParseTime(to);
        if (fromUtc is DateTimeOffset f && toUtc is DateTimeOffset t && t > f)
        {
            if (t - f > TimeSpan.FromDays(31))
                f = t - TimeSpan.FromDays(31);
            string step = MetricsRange.StepForDuration(t - f);
            return SnapToStep(new MetricsWindow(f, t, step, "custom", IsAbsolute: true));
        }

        string resolved = MetricsRange.Normalize(range, "1h");
        TimeSpan duration = MetricsRange.ToTimeSpan(resolved);
        string stepToken = MetricsRange.StepFor(resolved);
        DateTimeOffset end = now;
        DateTimeOffset start = end - duration;
        return SnapToStep(new MetricsWindow(start, end, stepToken, resolved, IsAbsolute: false));
    }

    /// <summary>
    /// Aligns window bounds to the step grid. Relative windows keep nominal duration
    /// (<c>end_snapped - duration</c>); absolute windows floor both ends independently.
    /// </summary>
    public static MetricsWindow SnapToStep(MetricsWindow window)
    {
        long stepSec = MetricsRange.ParseStepSeconds(window.Step);
        if (stepSec <= 1)
            return window;

        long endUnix = window.End.ToUnixTimeSeconds();
        long startUnix = window.Start.ToUnixTimeSeconds();
        long snappedEnd = MetricsRange.FloorUnixToStep(endUnix, stepSec);

        long snappedStart;
        if (window.IsAbsolute)
        {
            snappedStart = MetricsRange.FloorUnixToStep(startUnix, stepSec);
        }
        else
        {
            long durationSec = Math.Max(stepSec, endUnix - startUnix);
            snappedStart = snappedEnd - durationSec;
            // Duration tokens are multiples of step; keep start on-grid if clock skew intervened.
            snappedStart = MetricsRange.FloorUnixToStep(snappedStart, stepSec);
        }

        if (snappedStart >= snappedEnd)
            snappedStart = snappedEnd - stepSec;

        return window with
        {
            Start = DateTimeOffset.FromUnixTimeSeconds(snappedStart).ToUniversalTime(),
            End = DateTimeOffset.FromUnixTimeSeconds(snappedEnd).ToUniversalTime(),
        };
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
