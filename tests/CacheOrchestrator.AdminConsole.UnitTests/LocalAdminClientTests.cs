using System.Net;
using System.Text;
using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Models;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.AdminConsole.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class LocalAdminClientTests
{
    [Fact]
    public async Task GetHealthAsync_SendsApiKey_AndDeserializes()
    {
        RecordingHandler handler = new((req, _) =>
        {
            req.Method.Should().Be(HttpMethod.Get);
            req.RequestUri!.ToString().Should().Be("http://app-1:8080/cache-admin/local/health");
            req.Headers.Contains("X-Cache-Admin-Key").Should().BeTrue();
            req.Headers.GetValues("X-Cache-Admin-Key").Should().ContainSingle("secret-key");

            return JsonResponse("""{"healthy":true,"instanceId":"app-1","utcNow":"2026-01-01T00:00:00Z","adminEnabled":true}""");
        });

        LocalAdminClient sut = CreateSut(handler, apiKey: "secret-key");
        InstanceCallOutcome<AdminHealthDto> outcome = await sut.GetHealthAsync(
            new AdminInstanceOptions { Id = "app-1", Url = "http://app-1:8080" },
            TestContext.Current.CancellationToken);

        outcome.Succeeded.Should().BeTrue();
        outcome.Value!.Healthy.Should().BeTrue();
        outcome.Value.InstanceId.Should().Be("app-1");
        outcome.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetDomainsAsync_UsesConfiguredPathPrefix()
    {
        RecordingHandler handler = new((req, _) =>
        {
            req.RequestUri!.ToString().Should().Be("http://app-1:8080/custom/local/domains");
            return JsonResponse("""[{"name":"catalog","version":"1","dataCacheInstanceName":"default"}]""");
        });

        LocalAdminClient sut = CreateSut(handler, apiKey: "k", localPathPrefix: "/custom/local");
        InstanceCallOutcome<IReadOnlyList<AdminDomainConfigDto>> outcome = await sut.GetDomainsAsync(
            new AdminInstanceOptions { Id = "app-1", Url = "http://app-1:8080/" },
            TestContext.Current.CancellationToken);

        outcome.Succeeded.Should().BeTrue();
        outcome.Value.Should().ContainSingle(d => d.Name == "catalog");
    }

    [Fact]
    public async Task GetHealthAsync_HtmlResponse_FailsWithGuidance()
    {
        RecordingHandler handler = new((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>spa</html>", Encoding.UTF8, "text/html"),
            });

        LocalAdminClient sut = CreateSut(handler, apiKey: "k");
        InstanceCallOutcome<AdminHealthDto> outcome = await sut.GetHealthAsync(
            new AdminInstanceOptions { Id = "app-1", Url = "http://app-1:8080" },
            TestContext.Current.CancellationToken);

        outcome.Succeeded.Should().BeFalse();
        outcome.Error.Should().Contain("Non-JSON");
    }

    [Fact]
    public async Task SendAsync_ConflictClusterPublishIncomplete_ExposesPeerFailures()
    {
        RecordingHandler handler = new((_, _) => JsonResponse(
            """{"error":"Cluster publish incomplete.","localApplied":true,"peerFailures":[{"peerId":"b","error":"timeout"}]}""",
            HttpStatusCode.Conflict));

        LocalAdminClient sut = CreateSut(handler, apiKey: "k");
        InstanceCallOutcome<CacheInvalidationResult> outcome = await sut.InvalidateAsync(
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            new AdminInvalidateRequest { Scope = "domain", Domain = "catalog", Distribute = true },
            TestContext.Current.CancellationToken);

        outcome.Succeeded.Should().BeFalse();
        outcome.StatusCode.Should().Be(409);
        outcome.LocalApplied.Should().BeTrue();
        outcome.PeerFailures.Should().ContainSingle(p => p.PeerId == "b" && p.Error == "timeout");
        outcome.Error.Should().Contain("incomplete");
    }

    [Fact]
    public async Task GetHealthAsync_JsonErrorProperty_IsReturned()
    {
        RecordingHandler handler = new((_, _) => JsonResponse(
            """{"error":"bad api key"}""",
            HttpStatusCode.Unauthorized));

        LocalAdminClient sut = CreateSut(handler, apiKey: "wrong");
        InstanceCallOutcome<AdminHealthDto> outcome = await sut.GetHealthAsync(
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            TestContext.Current.CancellationToken);

        outcome.Succeeded.Should().BeFalse();
        outcome.Error.Should().Be("bad api key");
        outcome.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetHealthAsync_WhenApiKeyEmpty_DoesNotSendHeader()
    {
        RecordingHandler handler = new((req, _) =>
        {
            req.Headers.Contains("X-Cache-Admin-Key").Should().BeFalse();
            return JsonResponse("""{"healthy":true,"instanceId":"a","utcNow":"2026-01-01T00:00:00Z","adminEnabled":true}""");
        });

        LocalAdminClient sut = CreateSut(handler, apiKey: "");
        InstanceCallOutcome<AdminHealthDto> outcome = await sut.GetHealthAsync(
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            TestContext.Current.CancellationToken);
        outcome.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetHealthAsync_InvalidJson_Fails()
    {
        RecordingHandler handler = new((_, _) => JsonResponse("{not-json"));
        LocalAdminClient sut = CreateSut(handler, apiKey: "k");
        InstanceCallOutcome<AdminHealthDto> outcome = await sut.GetHealthAsync(
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            TestContext.Current.CancellationToken);

        outcome.Succeeded.Should().BeFalse();
        outcome.Error.Should().Contain("Invalid JSON");
    }

    [Fact]
    public async Task PatchSettingsAsync_EscapesDomainInUri()
    {
        RecordingHandler handler = new((req, _) =>
        {
            req.Method.Should().Be(HttpMethod.Patch);
            req.RequestUri!.ToString().Should().Be("http://a/cache-admin/local/domains/catalog%2Fv2/settings");
            return JsonResponse("""{"domain":"catalog/v2","effective":{"name":"catalog/v2","version":"1","dataCacheInstanceName":"default"}}""");
        });

        LocalAdminClient sut = CreateSut(handler, apiKey: "k");
        InstanceCallOutcome<AdminDomainMutationResultDto> outcome = await sut.PatchSettingsAsync(
            new AdminInstanceOptions { Id = "a", Url = "http://a" },
            "catalog/v2",
            new AdminSettingsPatchRequest
            {
                Settings = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["version"] = System.Text.Json.JsonSerializer.SerializeToElement("2"),
                },
            },
            TestContext.Current.CancellationToken);

        outcome.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetHealthAsync_MissingInstanceId_Throws()
    {
        LocalAdminClient sut = CreateSut(new RecordingHandler((_, _) => JsonResponse("{}")), apiKey: "k");
        Func<Task> act = () => sut.GetHealthAsync(
            new AdminInstanceOptions { Id = "", Url = "http://a" },
            TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("instance");
    }

    private static LocalAdminClient CreateSut(
        HttpMessageHandler handler,
        string apiKey,
        string localPathPrefix = "/cache-admin/local")
    {
        ServiceCollection services = new();
        services.AddHttpClient(LocalAdminClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        ServiceProvider sp = services.BuildServiceProvider();

        AdminConsoleOptions opts = new()
        {
            ApiKey = apiKey,
            LocalPathPrefix = localPathPrefix,
            RequestTimeoutMs = 5000,
        };

        return new LocalAdminClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            Microsoft.Extensions.Options.Options.Create(opts));
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request, cancellationToken));
    }
}
