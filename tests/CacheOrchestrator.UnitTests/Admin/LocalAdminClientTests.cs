using System.Net;
using System.Text;
using CacheOrchestrator.Admin;
using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.UnitTests.Admin;

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
            return JsonResponse("""[{"name":"catalog","version":"1","fusionCacheInstanceName":"default"}]""");
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
            Options.Create(opts));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
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
