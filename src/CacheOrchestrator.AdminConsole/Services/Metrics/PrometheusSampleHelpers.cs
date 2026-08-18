namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>Shared Prometheus sample helpers for Live and window-stats BFFs.</summary>
internal static class PrometheusSampleHelpers
{
    public static string Label(IReadOnlyDictionary<string, string> metric, string name)
    {
        if (metric.TryGetValue(name, out string? v) && !string.IsNullOrWhiteSpace(v))
            return v.Trim();
        return "";
    }

    public static long ToCount(double? v)
    {
        if (v is not double d || double.IsNaN(d) || double.IsInfinity(d) || d <= 0)
            return 0;
        return (long)Math.Round(d);
    }

    public static double? FirstValue(IReadOnlyList<PrometheusInstantSample> samples) =>
        samples.FirstOrDefault()?.Value is double v && !double.IsNaN(v) && !double.IsInfinity(v)
            ? Math.Max(0, v)
            : null;

    public static IReadOnlyList<string> ParseCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
