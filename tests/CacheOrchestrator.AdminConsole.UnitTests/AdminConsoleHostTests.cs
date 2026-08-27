using CacheOrchestrator.AdminConsole.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;

namespace CacheOrchestrator.AdminConsole.UnitTests;

/// <summary>
/// Smoke tests for the Admin Console host Minimal APIs (net10 only).
/// </summary>
public class AdminConsoleHostTests : IClassFixture<AdminConsoleHostTests.Factory>
{
    private readonly Factory _factory;

    public AdminConsoleHostTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("CacheOrchestrator.AdminConsole");
    }

    [Fact]
    public async Task About_ReturnsProduct()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/about", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Dictionary<string, object?>? payload =
            await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>(
                cancellationToken: TestContext.Current.CancellationToken);
        payload.Should().NotBeNull();
        payload["product"]?.ToString().Should().Contain("Admin Console");
    }

    [Fact]
    public async Task MetricsStatus_NotConfigured_ByDefault()
    {
        HttpClient client = _factory.CreateClient();
        MetricsStatusDto? status = await client.GetFromJsonAsync<MetricsStatusDto>(
            "/api/metrics/status",
            TestContext.Current.CancellationToken);
        status.Should().NotBeNull();
        status.Status.Should().Be(MetricsStoreStatusCodes.NotConfigured);
    }

    [Fact]
    public async Task StatsWindow_NotConfigured_ReturnsEnvelope()
    {
        HttpClient client = _factory.CreateClient();
        WindowStatsDto? window = await client.GetFromJsonAsync<WindowStatsDto>(
            "/api/stats/window?range=1h",
            TestContext.Current.CancellationToken);
        window.Should().NotBeNull();
        window.Status.Should().Be(MetricsStoreStatusCodes.NotConfigured);
        window.Domains.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalidate_MissingDomain_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/invalidate",
            new AdminConsoleInvalidateRequest
            {
                Scope = "domain",
                Domain = "",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Overview_ReturnsJsonWithStringHealthStatus()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/overview", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("\"status\":\"Down\"",
            "JsonStringEnumConverter must emit Healthy/Degraded/Down for the SPA");
        json.Should().Contain("\"id\":\"app-1\"");
    }

    [Fact]
    public async Task HintsRules_ReturnsCatalog()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/hints/rules", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("high-factory-share");
        json.Should().Contain("knownPaths");
    }

    [Fact]
    public async Task PatchSettings_EmptyBody_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PatchAsJsonAsync(
            "/api/domains/catalog/settings",
            new AdminConsoleSettingsPatchRequest { Settings = [] },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invalidate_UnknownScope_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/invalidate",
            new AdminConsoleInvalidateRequest { Scope = "all", Domain = "catalog" },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MetricsCatalog_ReturnsPanels()
    {
        HttpClient client = _factory.CreateClient();
        MetricsCatalogDto? catalog = await client.GetFromJsonAsync<MetricsCatalogDto>(
            "/api/metrics/catalog",
            TestContext.Current.CancellationToken);
        catalog.Should().NotBeNull();
        catalog.Status.Should().Be(MetricsStoreStatusCodes.NotConfigured);
        catalog.Panels.Should().BeEmpty("catalog panels are empty until a metrics store is configured");
    }

    [Fact]
    public async Task Distribution_WhenInstanceUnreachable_ReportsFanOut()
    {
        HttpClient client = _factory.CreateClient();
        ClusterDistributionCapabilityDto? cap = await client.GetFromJsonAsync<ClusterDistributionCapabilityDto>(
            "/api/distribution",
            TestContext.Current.CancellationToken);
        cap.Should().NotBeNull();
        cap.RecommendedMode.Should().Be(DistributionModes.FanOut);
        cap.BusAvailable.Should().BeFalse();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey, "Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AdminConsole:ApiKey"] = "test-key",
                    ["AdminConsole:Instances:0:Id"] = "app-1",
                    ["AdminConsole:Instances:0:Url"] = "http://127.0.0.1:9",
                    ["AdminConsole:Metrics:Enabled"] = "false",
                    ["AdminConsole:Hints:RuleFiles:0"] = "",
                    ["AdminConsole:Hints:DisabledStatePath"] = Path.Combine(
                        Path.GetTempPath(),
                        "co-admin-host-tests-disabled-" + Guid.NewGuid().ToString("N") + ".json"),
                });
            });
        }
    }
}
