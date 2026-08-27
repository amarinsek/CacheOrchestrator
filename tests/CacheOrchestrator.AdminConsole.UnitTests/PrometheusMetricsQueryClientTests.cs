using CacheOrchestrator.AdminConsole.Options;
using CacheOrchestrator.AdminConsole.Services.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace CacheOrchestrator.AdminConsole.UnitTests;

public class PrometheusMetricsQueryClientTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("prometheus", true)]
    [InlineData("Prometheus", true)]
    [InlineData("Victoria", false)]
    public void IsPrometheusProvider(string? provider, bool expected) =>
        PrometheusMetricsQueryClient.IsPrometheusProvider(provider).Should().Be(expected);

    [Theory]
    [InlineData(null, "/-/ready", "/-/ready")]
    [InlineData("", "/api/v1/query", "/api/v1/query")]
    [InlineData("prometheus", "/-/ready", "/prometheus/-/ready")]
    [InlineData("/prometheus/", "/api/v1/query", "/prometheus/api/v1/query")]
    public void CombinePath(string? prefix, string apiPath, string expected) =>
        PrometheusMetricsQueryClient.CombinePath(prefix, apiPath).Should().Be(expected);

    [Fact]
    public async Task ProbeAsync_WhenNotConfigured_Fails()
    {
        PrometheusMetricsQueryClient sut = CreateSut(
            new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)),
            new MetricsStoreOptions { Enabled = false });

        MetricsProbeResult result = await sut.ProbeAsync(TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("not configured");
    }

    [Fact]
    public async Task ProbeAsync_UnsupportedProvider_Fails()
    {
        PrometheusMetricsQueryClient sut = CreateSut(
            new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)),
            new MetricsStoreOptions
            {
                Enabled = true,
                BaseUrl = "http://prom:9090",
                Provider = "Influx",
            });

        MetricsProbeResult result = await sut.ProbeAsync(TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Unsupported");
    }

    [Fact]
    public async Task ProbeAsync_ReadyOk_Succeeds()
    {
        RecordingHandler handler = new((req, _) =>
        {
            req.RequestUri!.PathAndQuery.Should().Be("/-/ready");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        PrometheusMetricsQueryClient sut = CreateSut(handler, Configured());

        MetricsProbeResult result = await sut.ProbeAsync(TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeAsync_ReadyFails_FallsBackToBuildinfo()
    {
        RecordingHandler handler = new((req, _) =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/-/ready", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            req.RequestUri.PathAndQuery.Should().Contain("buildinfo");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        PrometheusMetricsQueryClient sut = CreateSut(handler, Configured());

        MetricsProbeResult result = await sut.ProbeAsync(TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task QueryRangeAsync_ParsesMatrix_AndSkipsNaN()
    {
        const string json = """
            {"status":"success","data":{"resultType":"matrix","result":[
              {"metric":{"domain":"catalog"},"values":[[100,"0.5"],[130,"NaN"],[160,"0.7"]]}
            ]}}
            """;
        RecordingHandler handler = new((req, _) =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.RequestUri!.PathAndQuery.Should().Be("/api/v1/query_range");
            return Json(json);
        });
        PrometheusMetricsQueryClient sut = CreateSut(handler, Configured());

        IReadOnlyList<PrometheusMatrixSeries> series = await sut.QueryRangeAsync(
            "up",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            "15s",
            TestContext.Current.CancellationToken);

        series.Should().ContainSingle();
        series[0].Metric["domain"].Should().Be("catalog");
        series[0].Points.Select(p => p.V).Should().Equal(0.5, 0.7);
    }

    [Fact]
    public async Task QueryInstantAsync_ParsesVector_AndSendsBearer()
    {
        const string json = """
            {"status":"success","data":{"resultType":"vector","result":[
              {"metric":{"__name__":"up"},"value":[100,"1"]}
            ]}}
            """;
        RecordingHandler handler = new((req, _) =>
        {
            req.Headers.Authorization.Should().NotBeNull();
            req.Headers.Authorization.Scheme.Should().Be("Bearer");
            req.Headers.Authorization.Parameter.Should().Be("tok");
            return Json(json);
        });
        PrometheusMetricsQueryClient sut = CreateSut(
            handler,
            new MetricsStoreOptions
            {
                Enabled = true,
                BaseUrl = "http://prom:9090",
                BearerToken = "tok",
            });

        IReadOnlyList<PrometheusInstantSample> samples = await sut.QueryInstantAsync(
            "up",
            cancellationToken: TestContext.Current.CancellationToken);

        samples.Should().ContainSingle();
        samples[0].Value.Should().Be(1);
    }

    [Fact]
    public async Task QueryInstantAsync_PrometheusErrorStatus_Throws()
    {
        RecordingHandler handler = new((_, _) =>
            Json("""{"status":"error","error":"bad_data"}"""));
        PrometheusMetricsQueryClient sut = CreateSut(handler, Configured());

        Func<Task> act = () => sut.QueryInstantAsync("up", cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bad_data*");
    }

    private static MetricsStoreOptions Configured() => new()
    {
        Enabled = true,
        BaseUrl = "http://prom:9090",
        Provider = "Prometheus",
    };

    private static PrometheusMetricsQueryClient CreateSut(HttpMessageHandler handler, MetricsStoreOptions metrics)
    {
        ServiceCollection services = new();
        services.AddHttpClient(PrometheusMetricsQueryClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("http://prom:9090/");
        }).ConfigurePrimaryHttpMessageHandler(() => handler);

        AdminConsoleOptions opts = new() { Metrics = metrics };
        return new PrometheusMetricsQueryClient(
            services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            Microsoft.Extensions.Options.Options.Create(opts));
    }

    private static HttpResponseMessage Json(string json) =>
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
