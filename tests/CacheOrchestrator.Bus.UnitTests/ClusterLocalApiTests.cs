using CacheOrchestrator.Bus;
using CacheOrchestrator.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CacheOrchestrator.Bus.UnitTests;

public class ClusterLocalApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Map_WhenBusDisabled_DoesNotExposeApply()
    {
        using IHost host = await CreateHostAsync(busEnabled: false, adminEnabled: false);
        HttpClient client = host.GetTestClient();

        HttpResponseMessage response = await client.PostAsync("/cache-admin/local/cluster/apply", new StringContent("{}"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Apply_WhenApiKeyMissing_ReturnsUnauthorized()
    {
        using IHost host = await CreateHostAsync(busEnabled: true, adminEnabled: false, apiKey: "secret");
        HttpClient client = host.GetTestClient();

        HttpResponseMessage response = await client.PostAsync(
            "/cache-admin/local/cluster/apply",
            JsonBody(origin: "peer-1"),
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Apply_WhenJsonInvalid_ReturnsBadRequest()
    {
        using IHost host = await CreateHostAsync(busEnabled: true, adminEnabled: false, apiKey: "k");
        HttpClient client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ClusterEndpointAuth.HeaderName, "k");

        using StringContent body = new("{not-json", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync("/cache-admin/local/cluster/apply", body, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Apply_WhenNamespaceMismatch_ReturnsConflict()
    {
        using IHost host = await CreateHostAsync(busEnabled: true, adminEnabled: false, apiKey: "k");
        HttpClient client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ClusterEndpointAuth.HeaderName, "k");

        HttpResponseMessage response = await client.PostAsync(
            "/cache-admin/local/cluster/apply",
            JsonBody(origin: "peer-1", ns: "other-ns"),
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Apply_WhenOriginIsSelf_ReturnsNotApplied()
    {
        using IHost host = await CreateHostAsync(
            busEnabled: true, adminEnabled: false, apiKey: "k", instanceId: "self-1");
        HttpClient client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ClusterEndpointAuth.HeaderName, "k");

        HttpResponseMessage response = await client.PostAsync(
            "/cache-admin/local/cluster/apply",
            JsonBody(origin: "self-1"),
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("applied").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("reason").GetString().Should().Be("origin-is-self");
    }

    [Fact]
    public async Task Apply_WhenValidRemoteCommand_Applies()
    {
        using IHost host = await CreateHostAsync(
            busEnabled: true, adminEnabled: false, apiKey: "k", instanceId: "self-1");
        HttpClient client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ClusterEndpointAuth.HeaderName, "k");

        HttpResponseMessage response = await client.PostAsync(
            "/cache-admin/local/cluster/apply",
            JsonBody(origin: "peer-1"),
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("applied").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Info_WhenAdminDisabled_IsMapped()
    {
        using IHost host = await CreateHostAsync(
            busEnabled: true, adminEnabled: false, apiKey: "k", instanceId: "self-1");
        HttpClient client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(ClusterEndpointAuth.HeaderName, "k");

        HttpResponseMessage response = await client.GetAsync("/cache-admin/local/cluster/info", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        doc.RootElement.GetProperty("instanceId").GetString().Should().Be("self-1");
        doc.RootElement.GetProperty("busEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void MapCacheOrchestratorHttpBus_WhenEndpointsNull_Throws()
    {
        var act = () => ApplicationBuilderExtensions.MapCacheOrchestratorHttpBus(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static StringContent JsonBody(string origin, string ns = "app1")
    {
        string json = $$"""
            {
              "commandType": "invalidate",
              "commandId": "{{Guid.NewGuid()}}",
              "originInstanceId": "{{origin}}",
              "namespace": "{{ns}}",
              "timestampUtc": "{{DateTimeOffset.UtcNow:O}}",
              "kind": 0,
              "scope": "products",
              "tags": ["domain:products"],
              "domain": "products"
            }
            """;
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task<IHost> CreateHostAsync(
        bool busEnabled,
        bool adminEnabled,
        string? apiKey = null,
        string instanceId = "self-1")
    {
        Dictionary<string, string?> data = new()
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
            ["Cache:Namespace"] = "app1",
            ["Cache:InstanceId"] = instanceId,
            ["Cache:Admin:Enabled"] = adminEnabled ? "true" : "false",
            ["Cache:Cluster:Bus:Enabled"] = busEnabled ? "true" : "false",
            ["Cache:Cluster:Bus:Membership"] = "Static"
        };
        if (apiKey is not null)
            data["Cache:Cluster:Bus:ApiKey"] = apiKey;

        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(data).Build();

        IHostBuilder builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton(config);
                    services.AddLogging();
                    services.AddRouting();
                    services.AddCacheOrchestrator(config, o => o.AddHttpClusterBus(), enableMvcConvention: false);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapCacheOrchestratorHttpBus());
                });
            });

        IHost host = builder.Build();
        await host.StartAsync(Ct);
        return host;
    }
}
