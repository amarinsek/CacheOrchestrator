using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Services.Metrics;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class MetricsPanelCatalogTests
{
    [Fact]
    public void BuildPromQl_request_rate_includes_metric_name()
    {
        string q = MetricsPanelCatalog.BuildPromQl("request_rate", domains: null);
        Assert.Contains(MetricsPanelCatalog.OcRequests, q, StringComparison.Ordinal);
        Assert.Contains("rate(", q, StringComparison.Ordinal);
        Assert.Contains("sum by (domain)", q, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPromQl_domain_filter_sanitized_regex()
    {
        string q = MetricsPanelCatalog.BuildPromQl("oc_hit_share", ["catalog", "bad;drop"]);
        Assert.Contains("domain=~\"catalog|baddrop\"", q, StringComparison.Ordinal);
        Assert.Contains("result=\"hit\"", q, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPromQl_route_and_instance_filters()
    {
        string q = MetricsPanelCatalog.BuildPromQl(
            "request_rate",
            domains: null,
            instanceIds: ["app-1"],
            routes: ["GET /api/catalog"]);
        Assert.Contains("instance_id=~\"app-1\"", q, StringComparison.Ordinal);
        Assert.Contains("route=~\"GET /api/catalog\"", q, StringComparison.Ordinal);
        Assert.Contains("sum by (route)", q, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPromQl_unknown_panel_throws()
    {
        Assert.Throws<ArgumentException>(() => MetricsPanelCatalog.BuildPromQl("nope", null));
    }

    [Fact]
    public void BuildWindowCountPromQl_UsesLastOverTimeDelta_NotBareIncrease()
    {
        // Bare increase() under-counts the first sample and can return 0 that blocks PromQL `or`,
        // so a new series appears once then vanishes on the next scrape (Console table bug).
        string q = MetricsPanelCatalog.BuildWindowCountPromQl(
            "domain,result",
            MetricsPanelCatalog.OcRequests,
            "900s");

        Assert.Contains("last_over_time(", q, StringComparison.Ordinal);
        Assert.Contains("offset 900s", q, StringComparison.Ordinal);
        Assert.Contains("unless on (domain,result)", q, StringComparison.Ordinal);
        Assert.DoesNotContain("increase(", q, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPromQl_factory_panels()
    {
        string p95 = MetricsPanelCatalog.BuildPromQl("factory_p95_ms", ["catalog"]);
        Assert.Contains(MetricsPanelCatalog.FactoryDurationBucket, p95, StringComparison.Ordinal);
        Assert.Contains("histogram_quantile(0.95", p95, StringComparison.Ordinal);

        string rate = MetricsPanelCatalog.BuildPromQl("factory_run_rate", null);
        Assert.Contains("result=\"miss\"", rate, StringComparison.Ordinal);

        string share = MetricsPanelCatalog.BuildPromQl("factory_share", null);
        Assert.Contains(MetricsPanelCatalog.OcRequests, share, StringComparison.Ordinal);
        Assert.Contains("result=\"miss\"", share, StringComparison.Ordinal);

        string stale = MetricsPanelCatalog.BuildPromQl("fc_stale_share", null);
        Assert.Contains(MetricsPanelCatalog.FcRequests, stale, StringComparison.Ordinal);
        Assert.Contains(MetricsPanelCatalog.OcRequests, stale, StringComparison.Ordinal);
        Assert.Contains("result=\"stale\"", stale, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeLabelValue_strips_injection_chars()
    {
        Assert.Equal("products_v1", MetricsPanelCatalog.SanitizeLabelValue("products_v1"));
        Assert.Equal("ab", MetricsPanelCatalog.SanitizeLabelValue("a b;()"));
    }

    [Theory]
    [InlineData("1h", "1h")]
    [InlineData("24H", "24h")]
    [InlineData("nope", "1h")]
    [InlineData(null, "1h")]
    public void MetricsRange_Normalize(string? input, string expected)
    {
        Assert.Equal(expected, MetricsRange.Normalize(input));
    }

    [Fact]
    public void CombinePath_prefixes_api()
    {
        Assert.Equal("/api/v1/query", PrometheusMetricsQueryClient.CombinePath(null, "/api/v1/query"));
        Assert.Equal("/prometheus/api/v1/query", PrometheusMetricsQueryClient.CombinePath("/prometheus", "/api/v1/query"));
        Assert.Equal("/prometheus/api/v1/query", PrometheusMetricsQueryClient.CombinePath("prometheus/", "/api/v1/query"));
    }
}
