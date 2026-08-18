using System.Net;
using System.Net.Http.Json;
using CacheOrchestrator.AdminConsole.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CacheOrchestrator.AdminConsole.UnitTests;

/// <summary>
/// Smoke tests for the Admin Console host Minimal APIs (net10 only).
/// </summary>
public class AdminConsoleHostTests : IClassFixture<AdminConsoleHostTests.Factory>
{
    private readonly Factory _factory;

    public AdminConsoleHostTests(Factory factory) => _factory = factory;

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
        payload!["product"]?.ToString().Should().Contain("Admin Console");
    }

    [Fact]
    public async Task MetricsStatus_NotConfigured_ByDefault()
    {
        HttpClient client = _factory.CreateClient();
        MetricsStatusDto? status = await client.GetFromJsonAsync<MetricsStatusDto>(
            "/api/metrics/status",
            TestContext.Current.CancellationToken);
        status.Should().NotBeNull();
        status!.Status.Should().Be(MetricsStoreStatusCodes.NotConfigured);
    }

    [Fact]
    public async Task StatsWindow_NotConfigured_ReturnsEnvelope()
    {
        HttpClient client = _factory.CreateClient();
        WindowStatsDto? window = await client.GetFromJsonAsync<WindowStatsDto>(
            "/api/stats/window?range=1h",
            TestContext.Current.CancellationToken);
        window.Should().NotBeNull();
        window!.Status.Should().Be(MetricsStoreStatusCodes.NotConfigured);
        window.Domains.Should().BeEmpty();
    }


    [Fact]
    public async Task Invalidate_UnknownInstance_ReturnsNotFound()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/invalidate",
            new AdminConsoleInvalidateRequest
            {
                Target = "instance:missing",
                Scope = "domain",
                Domain = "catalog",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalidate_MissingDomain_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/invalidate",
            new AdminConsoleInvalidateRequest
            {
                Target = "all",
                Scope = "domain",
                Domain = "",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
