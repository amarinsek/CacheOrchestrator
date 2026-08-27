using CacheOrchestrator.Configuration;
using CacheOrchestrator.HttpBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.HttpBus.UnitTests;

public class ClusterEndpointAuthTests
{
    [Fact]
    public async Task InvokeAsync_WhenNoApiKeyConfigured_CallsNext()
    {
        ClusterEndpointAuth auth = Create(apiKey: null);
        object? result = await auth.InvokeAsync(
            new FakeFilterContext(new DefaultHttpContext()),
            _ => ValueTask.FromResult<object?>("ok"));

        result.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsync_WhenHeaderMissing_ReturnsUnauthorized()
    {
        ClusterEndpointAuth auth = Create(apiKey: "secret");
        object? result = await auth.InvokeAsync(
            new FakeFilterContext(new DefaultHttpContext()),
            _ => ValueTask.FromResult<object?>("ok"));

        result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task InvokeAsync_WhenHeaderWrong_ReturnsUnauthorized()
    {
        DefaultHttpContext http = new();
        http.Request.Headers[ClusterEndpointAuth.HeaderName] = "nope";
        ClusterEndpointAuth auth = Create(apiKey: "secret");

        object? result = await auth.InvokeAsync(
            new FakeFilterContext(http),
            _ => ValueTask.FromResult<object?>("ok"));

        result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task InvokeAsync_WhenHeaderMatches_CallsNext()
    {
        DefaultHttpContext http = new();
        http.Request.Headers[ClusterEndpointAuth.HeaderName] = "secret";
        ClusterEndpointAuth auth = Create(apiKey: "secret");

        object? result = await auth.InvokeAsync(
            new FakeFilterContext(http),
            _ => ValueTask.FromResult<object?>("ok"));

        result.Should().Be("ok");
    }

    private static ClusterEndpointAuth Create(string? apiKey)
    {
        IOptionsMonitor<CacheOrchestratorOptions> monitor = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        CacheOrchestratorOptions options = new();
        options.Cluster.Bus.ApiKey = apiKey;
        monitor.CurrentValue.Returns(options);
        return new ClusterEndpointAuth(monitor);
    }

    private sealed class FakeFilterContext(HttpContext http) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = http;
        public override IList<object?> Arguments { get; } = [];
        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }
}
