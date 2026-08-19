using System.Net;
using System.Net.Http.Json;
using CacheOrchestrator.Admin;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Behavior;

/// <summary>
/// Local Admin <c>GET …/health</c> is not ASP.NET health checks: HTTP 200 with
/// <c>Healthy: false</c> means degraded (Admin Console maps that). ASP.NET
/// <c>AddCacheOrchestrator</c> on <c>IHealthChecksBuilder</c> is a separate surface.
/// </summary>
public class AdminHealthHttpTests
{
    private sealed class FailingProbe : ICacheOrchestratorHealthProbe
    {
        public string Name => "failing-it";

        public Task ProbeAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("probe-failed");
    }

    private static async Task<(HttpClient Client, WebApplication App)> StartAsync(
        bool registerFailingProbe,
        bool mapAspNetHealthChecks)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
                ["Cache:InstanceId"] = "it-admin-health",
                ["Cache:Admin:Enabled"] = "true",
                ["Cache:Admin:ApiKey"] = "k",
                ["Cache:Admin:RoutePrefix"] = "/cache-admin/local",
                ["Cache:Domains:catalog:Version"] = "v1",
                ["Cache:Domains:catalog:OutputCacheTtlSeconds"] = "60",
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config, enableMvcConvention: false);
        if (registerFailingProbe)
            builder.Services.AddSingleton<ICacheOrchestratorHealthProbe, FailingProbe>();
        if (mapAspNetHealthChecks)
            builder.Services.AddHealthChecks().AddCacheOrchestrator();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.MapCacheOrchestratorAdmin();
        if (mapAspNetHealthChecks)
        {
            app.MapHealthChecks("/ready", new HealthCheckOptions
            {
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
                },
            });
        }

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Cache-Admin-Key", "k");
        return (client, app);
    }

    [Fact]
    public async Task AdminHealth_WhenProbesSucceed_Returns200HealthyTrue()
    {
        (HttpClient? client, WebApplication? app) = await StartAsync(
            registerFailingProbe: false,
            mapAspNetHealthChecks: false);

        try
        {
            HttpResponseMessage response = await client.GetAsync(
                "/cache-admin/local/health",
                TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            AdminHealthDto? body = await response.Content.ReadFromJsonAsync<AdminHealthDto>(
                cancellationToken: TestContext.Current.CancellationToken);
            body.Should().NotBeNull();
            body!.Healthy.Should().BeTrue();
            body.AdminEnabled.Should().BeTrue();
            body.InstanceId.Should().Be("it-admin-health");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AdminHealth_WhenProbeFails_Returns200HealthyFalse()
    {
        (HttpClient? client, WebApplication? app) = await StartAsync(
            registerFailingProbe: true,
            mapAspNetHealthChecks: false);

        try
        {
            HttpResponseMessage response = await client.GetAsync(
                "/cache-admin/local/health",
                TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "Admin health is always HTTP 200 when the endpoint is authorized; degraded is Healthy=false");

            AdminHealthDto? body = await response.Content.ReadFromJsonAsync<AdminHealthDto>(
                cancellationToken: TestContext.Current.CancellationToken);
            body.Should().NotBeNull();
            body!.Healthy.Should().BeFalse();
            body.AdminEnabled.Should().BeTrue();
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AspNetHealthChecks_WhenProbeFails_AreDegraded()
    {
        (HttpClient? client, WebApplication? app) = await StartAsync(
            registerFailingProbe: true,
            mapAspNetHealthChecks: true);

        try
        {
            HttpResponseMessage admin = await client.GetAsync(
                "/cache-admin/local/health",
                TestContext.Current.CancellationToken);
            admin.StatusCode.Should().Be(HttpStatusCode.OK);
            AdminHealthDto? dto = await admin.Content.ReadFromJsonAsync<AdminHealthDto>(
                cancellationToken: TestContext.Current.CancellationToken);
            dto!.Healthy.Should().BeFalse();

            HttpResponseMessage ready = await client.GetAsync("/ready", TestContext.Current.CancellationToken);
            ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
                "ASP.NET AddCacheOrchestrator health checks use Degraded (mapped to 503 in this host)");
            string text = await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            bool degraded = text.Contains("Degraded", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase);
            degraded.Should().BeTrue($"ASP.NET health body should report Degraded/Unhealthy, was '{text}'");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
