using System.Text;
using CacheOrchestrator.Admin.App.Models;

namespace CacheOrchestrator.Admin.App.Services.Metrics;

/// <summary>
/// Allowlisted Metrics panels and PromQL builders.
/// Metric names match OpenTelemetry → Prometheus export of the CacheOrchestrator meter
/// (<c>cache_orchestrator.*</c> instruments → <c>cache_orchestrator_*_total</c> counters).
/// </summary>
public static class MetricsPanelCatalog
{
    /// <summary>Default OTel/Prometheus counter names (dots → underscores, <c>_total</c>).</summary>
    public const string OcRequests = "cache_orchestrator_oc_requests_total";
    public const string FcRequests = "cache_orchestrator_fc_requests_total";
    public const string Invalidations = "cache_orchestrator_invalidate_total";
    public const string ClientSchedule = "cache_orchestrator_client_schedule_total";
    public const string ClusterPublishFailures = "cache_orchestrator_cluster_publish_failures_total";

    /// <summary>Histogram buckets for Fusion duration (unit ms → milliseconds in OTel export).</summary>
    public const string FcDurationBucket = "cache_orchestrator_fc_duration_milliseconds_bucket";

    private static readonly IReadOnlyList<MetricsPanelInfoDto> All =
    [
        new()
        {
            Id = "request_rate",
            Title = "Request rate",
            Description = "Output Cache outcomes per second (proxy for request volume).",
            Unit = "rate",
        },
        new()
        {
            Id = "oc_hit_share",
            Title = "OC hit share",
            Description = "Share of Output Cache hits among OC outcomes in the window.",
            Unit = "percent",
        },
        new()
        {
            Id = "fc_hit_rate",
            Title = "FC hit rate",
            Description = "FusionCache hit rate among FC operations (not request share).",
            Unit = "percent",
        },
        new()
        {
            Id = "invalidation_rate",
            Title = "Invalidations",
            Description = "Successful domain invalidations per second.",
            Unit = "rate",
        },
        new()
        {
            Id = "schedule_phase",
            Title = "Client Cache Schedule",
            Description = "Client schedule phase application rate by phase.",
            Unit = "rate",
        },
        new()
        {
            Id = "cluster_publish_failures",
            Title = "Cluster publish failures",
            Description = "Per-peer cluster bus publish failures per second.",
            Unit = "rate",
        },
        new()
        {
            Id = "fc_p95_ms",
            Title = "FC duration p95",
            Description = "Fusion GetOrSet duration p95 (ms). Requires histogram scrape.",
            Unit = "ms",
        },
    ];

    /// <summary>All allowlisted panels.</summary>
    public static IReadOnlyList<MetricsPanelInfoDto> Panels => All;

    /// <summary>Looks up panel metadata by id (case-insensitive).</summary>
    public static MetricsPanelInfoDto? Find(string? panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId))
            return null;
        return All.FirstOrDefault(p =>
            string.Equals(p.Id, panelId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds PromQL for a panel. <paramref name="domains"/> is an optional allow-list (OR regex).
    /// Empty/null domains → aggregate by domain label when the metric has one; schedule uses phase.
    /// </summary>
    public static string BuildPromQl(string panelId, IReadOnlyList<string>? domains, string rateWindow = "5m")
    {
        MetricsPanelInfoDto panel = Find(panelId)
            ?? throw new ArgumentException($"Unknown metrics panel '{panelId}'.", nameof(panelId));

        string domainMatcher = BuildDomainMatcher(domains);
        string rw = SanitizeDuration(rateWindow);

        return panel.Id switch
        {
            "request_rate" =>
                $"sum by (domain) (rate({OcRequests}{{{domainMatcher}}}[{rw}]))",

            "oc_hit_share" =>
                $"sum by (domain) (rate({OcRequests}{{result=\"hit\"{AndDomain(domainMatcher)}}}[{rw}]))" +
                $" / clamp_min(sum by (domain) (rate({OcRequests}{{{domainMatcher}}}[{rw}])), 1e-9)",

            "fc_hit_rate" =>
                $"sum by (domain) (rate({FcRequests}{{result=\"hit\"{AndDomain(domainMatcher)}}}[{rw}]))" +
                $" / clamp_min(sum by (domain) (rate({FcRequests}{{{domainMatcher}}}[{rw}])), 1e-9)",

            "invalidation_rate" =>
                $"sum by (domain) (rate({Invalidations}{{{domainMatcher}}}[{rw}]))",

            "schedule_phase" =>
                $"sum by (phase) (rate({ClientSchedule}{{{domainMatcher}}}[{rw}]))",

            "cluster_publish_failures" =>
                $"sum by (reason) (rate({ClusterPublishFailures}[{rw}]))",

            "fc_p95_ms" =>
                "histogram_quantile(0.95, " +
                $"sum by (le, domain) (rate({FcDurationBucket}{{{domainMatcher}}}[{rw}])))",

            _ => throw new ArgumentException($"Unknown metrics panel '{panelId}'.", nameof(panelId)),
        };
    }

    /// <summary>Instant PromQL used for summary KPIs (no by-domain).</summary>
    public static string BuildSummaryPromQl(string panelId, string rateWindow = "5m")
    {
        string rw = SanitizeDuration(rateWindow);
        return panelId switch
        {
            "request_rate" => $"sum(rate({OcRequests}[{rw}]))",
            "oc_hit_share" =>
                $"sum(rate({OcRequests}{{result=\"hit\"}}[{rw}]))" +
                $" / clamp_min(sum(rate({OcRequests}[{rw}])), 1e-9)",
            "fc_hit_rate" =>
                $"sum(rate({FcRequests}{{result=\"hit\"}}[{rw}]))" +
                $" / clamp_min(sum(rate({FcRequests}[{rw}])), 1e-9)",
            "invalidation_rate" => $"sum(rate({Invalidations}[{rw}]))",
            _ => throw new ArgumentException($"No summary query for panel '{panelId}'.", nameof(panelId)),
        };
    }

    private static string BuildDomainMatcher(IReadOnlyList<string>? domains)
    {
        if (domains is null || domains.Count == 0)
            return "";

        StringBuilder sb = new("domain=~\"");
        bool first = true;
        foreach (string raw in domains)
        {
            string d = SanitizeLabelValue(raw);
            if (d.Length == 0)
                continue;
            if (!first)
                sb.Append('|');
            sb.Append(d);
            first = false;
        }

        if (first)
            return "";

        sb.Append('"');
        return sb.ToString();
    }

    private static string AndDomain(string domainMatcher) =>
        string.IsNullOrEmpty(domainMatcher) ? "" : "," + domainMatcher;

    /// <summary>Only allow safe label fragments (alnum, underscore, hyphen, dot).</summary>
    public static string SanitizeLabelValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        StringBuilder sb = new();
        foreach (char c in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')
                sb.Append(c);
        }

        return sb.ToString();
    }

    internal static string SanitizeDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return "5m";
        string d = duration.Trim();
        // 15s, 5m, 1h, 7d
        if (d.Length is < 2 or > 8)
            return "5m";
        for (int i = 0; i < d.Length - 1; i++)
        {
            if (!char.IsAsciiDigit(d[i]))
                return "5m";
        }

        char unit = d[^1];
        if (unit is not ('s' or 'm' or 'h' or 'd'))
            return "5m";
        return d;
    }
}
