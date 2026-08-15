using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Services.Metrics;

namespace CacheOrchestrator.UnitTests.Admin;

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
