using System.Text;
using CacheOrchestrator.AdminConsole.Models;

namespace CacheOrchestrator.AdminConsole.Services.Metrics;

/// <summary>
/// Allowlisted Metrics panels and PromQL builders.
/// Metric names match OpenTelemetry → Prometheus export of the CacheOrchestrator meter
/// (<c>cache_orchestrator.*</c> instruments → <c>cache_orchestrator_*_total</c> counters).
/// Optional <c>route</c> label when apps enable <c>Cache:Metrics:IncludeEndpointLabel</c>.
/// Instance filter uses scrape label <c>instance_id</c> (align with Admin Console App instance ids).
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

    /// <summary>Scrape label for Admin Console App instance id (see samples/…/labs topology labs).</summary>
    public const string InstanceIdLabel = "instance_id";

    /// <summary>OTel tag for stable endpoint key.</summary>
    public const string RouteLabel = "route";

    private static readonly IReadOnlyList<MetricsPanelInfoDto> All =
    [
        new()
        {
            Id = "request_rate",
            Title = "Request rate",
            Description =
                "How many HTTP cache outcomes happen per second in this window. Higher means more traffic through Output Cache instrumentation (a good proxy for overall request volume).",
            Unit = "rate",
        },
        new()
        {
            Id = "oc_hit_share",
            Title = "OC hit share",
            Description =
                "Of responses that hit Output Cache accounting in this window, what fraction were served from the full HTTP response cache (OC hit). Higher is better — fewer requests reach the app handler.",
            Unit = "percent",
        },
        new()
        {
            Id = "fc_hit_rate",
            Title = "FC hit rate",
            Description =
                "Of FusionCache operations in this window (when the data path ran), what fraction were hits. This is a layer rate, not share of all HTTP requests — low can be normal if Output Cache already absorbs most traffic.",
            Unit = "percent",
        },
        new()
        {
            Id = "invalidation_rate",
            Title = "Invalidations",
            Description =
                "How often successful domain (or related) invalidations run per second. Spikes mean more cache purge pressure and usually more factory/origin work afterward.",
            Unit = "rate",
        },
        new()
        {
            Id = "schedule_phase",
            Title = "Client Cache Schedule",
            Description =
                "How often each Client Cache Schedule phase is applied when writing client Cache-Control: Calm (long max-age), Approaching (ramping down), or Hold (floor max-age near cutover).",
            Unit = "rate",
        },
        new()
        {
            Id = "cluster_publish_failures",
            Title = "Cluster publish failures",
            Description =
                "How often cluster-bus publish to a peer fails per second. Rising values mean invalidation or runtime commands may not reach other instances.",
            Unit = "rate",
        },
        new()
        {
            Id = "fc_p95_ms",
            Title = "FC duration p95",
            Description =
                "95th percentile time spent in Fusion GetOrSet (milliseconds) in this window — how slow the slow factory/cache path feels. Needs histogram scrape of Fusion duration.",
            Unit = "ms",
        },
    ];

    /// <summary>Default Metrics page panels.</summary>
    public static readonly IReadOnlyList<string> DefaultPagePanels =
    [
        "request_rate",
        "oc_hit_share",
        "fc_hit_rate",
        "invalidation_rate",
        "schedule_phase",
        "cluster_publish_failures",
        "fc_p95_ms",
    ];

    /// <summary>Domain detail panels.</summary>
    public static readonly IReadOnlyList<string> DomainDetailPanels =
    [
        "request_rate",
        "oc_hit_share",
        "fc_hit_rate",
        "invalidation_rate",
        "schedule_phase",
        "fc_p95_ms",
    ];

    /// <summary>Instance detail panels.</summary>
    public static readonly IReadOnlyList<string> InstanceDetailPanels =
    [
        "request_rate",
        "oc_hit_share",
        "fc_hit_rate",
        "invalidation_rate",
        "fc_p95_ms",
        "cluster_publish_failures",
    ];

    /// <summary>Endpoint detail panels (require <c>route</c> label samples).</summary>
    public static readonly IReadOnlyList<string> EndpointDetailPanels =
    [
        "request_rate",
        "oc_hit_share",
        "fc_hit_rate",
        "fc_p95_ms",
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
    /// Builds PromQL for a panel with optional domain, instance_id, and route filters.
    /// </summary>
    public static string BuildPromQl(
        string panelId,
        IReadOnlyList<string>? domains,
        IReadOnlyList<string>? instanceIds = null,
        IReadOnlyList<string>? routes = null,
        string rateWindow = "5m")
    {
        MetricsPanelInfoDto panel = Find(panelId)
            ?? throw new ArgumentException($"Unknown metrics panel '{panelId}'.", nameof(panelId));

        string selector = BuildLabelSelector(domains, instanceIds, routes);
        string selectorHit = BuildLabelSelector(domains, instanceIds, routes, extra: "result=\"hit\"");
        string rw = SanitizeDuration(rateWindow);
        string by = ChooseByClause(panel.Id, domains, routes);

        return panel.Id switch
        {
            "request_rate" =>
                $"{by} (rate({OcRequests}{selector}[{rw}]))",

            "oc_hit_share" =>
                $"{by} (rate({OcRequests}{selectorHit}[{rw}]))" +
                $" / clamp_min({by} (rate({OcRequests}{selector}[{rw}])), 1e-9)",

            "fc_hit_rate" =>
                $"{by} (rate({FcRequests}{selectorHit}[{rw}]))" +
                $" / clamp_min({by} (rate({FcRequests}{selector}[{rw}])), 1e-9)",

            "invalidation_rate" =>
                $"{by} (rate({Invalidations}{selector}[{rw}]))",

            "schedule_phase" =>
                // phase breakdown; domain/instance filters only (no route on this instrument)
                $"sum by (phase) (rate({ClientSchedule}{BuildLabelSelector(domains, instanceIds, routes: null)}[{rw}]))",

            "cluster_publish_failures" =>
                $"sum by (reason) (rate({ClusterPublishFailures}{BuildLabelSelector(domains: null, instanceIds, routes: null)}[{rw}]))",

            "fc_p95_ms" =>
                "histogram_quantile(0.95, " +
                $"sum by (le{(routes is { Count: > 0 } ? ", route" : domains is { Count: > 0 } ? ", domain" : "")}) " +
                $"(rate({FcDurationBucket}{selector}[{rw}])))",

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

    private static string ChooseByClause(
        string panelId,
        IReadOnlyList<string>? domains,
        IReadOnlyList<string>? routes)
    {
        if (panelId is "schedule_phase" or "cluster_publish_failures")
            return "sum by (phase)"; // overridden in switch for reason/phase

        if (routes is { Count: > 0 })
            return "sum by (route)";
        if (domains is { Count: 1 })
            return "sum";
        return "sum by (domain)";
    }

    private static string BuildLabelSelector(
        IReadOnlyList<string>? domains,
        IReadOnlyList<string>? instanceIds,
        IReadOnlyList<string>? routes,
        string? extra = null)
    {
        List<string> parts = [];
        if (extra is { Length: > 0 })
            parts.Add(extra);

        string d = BuildRegexMatcher("domain", domains);
        if (d.Length > 0) parts.Add(d);

        string i = BuildRegexMatcher(InstanceIdLabel, instanceIds);
        if (i.Length > 0) parts.Add(i);

        // Route values contain spaces (METHOD pattern) — quote carefully.
        string r = BuildRegexMatcher(RouteLabel, routes, allowSpace: true);
        if (r.Length > 0) parts.Add(r);

        if (parts.Count == 0)
            return "";
        return "{" + string.Join(",", parts) + "}";
    }

    private static string BuildRegexMatcher(string label, IReadOnlyList<string>? values, bool allowSpace = false)
    {
        if (values is null || values.Count == 0)
            return "";

        StringBuilder sb = new();
        sb.Append(label).Append("=~\"");
        bool first = true;
        foreach (string raw in values)
        {
            string v = allowSpace ? SanitizeRouteLabelValue(raw) : SanitizeLabelValue(raw);
            if (v.Length == 0)
                continue;
            if (!first)
                sb.Append('|');
            // Escape regex metacharacters for exact-ish match of fixed keys.
            sb.Append(RegexEscape(v));
            first = false;
        }

        if (first)
            return "";
        sb.Append('"');
        return sb.ToString();
    }

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

    /// <summary>Sanitize Admin endpoint keys: <c>GET /api/foo/{id}</c>.</summary>
    public static string SanitizeRouteLabelValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        StringBuilder sb = new();
        foreach (char c in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ' ' or '{' or '}' or ':')
                sb.Append(c);
        }

        return sb.ToString();
    }

    internal static string SanitizeDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return "5m";
        string d = duration.Trim();
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

    private static string RegexEscape(string s)
    {
        // Escape PromQL/RE2 metacharacters in fixed route/domain keys.
        StringBuilder sb = new(s.Length * 2);
        foreach (char c in s)
        {
            if (c is '.' or '+' or '*' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|' or '^' or '$' or '\\')
                sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }
}
