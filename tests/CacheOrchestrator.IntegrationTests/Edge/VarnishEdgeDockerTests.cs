using CacheOrchestrator.Edge.DependencyInjection;
using CacheOrchestrator.Edge.Providers;
using CacheOrchestrator.Edge.Varnish;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Edge;

[Collection("Varnish")]
public sealed class VarnishEdgeDockerTests(VarnishFixture varnish)
{
    [Fact]
    public async Task XkeyInvalidation_ChangesHitToMiss()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:EdgeInstances:edge:Provider"] = "Varnish",
            ["Cache:EdgeInstances:edge:Varnish:PurgeUrl"] =
                new Uri(varnish.Address, "/cache-orchestrator/purge").AbsoluteUri,
            ["Cache:EdgeInstances:edge:Varnish:ApiKey"] = "integration-secret",
            ["Cache:Domains:catalog:Edge:Enabled"] = "true",
            ["Cache:Domains:catalog:Edge:Instance"] = "edge",
            ["Cache:Domains:catalog:Edge:TtlSeconds"] = "300",
            ["Cache:Domains:catalog:Edge:StaleWhileRevalidateSeconds"] = "30"
        });
        builder.Services.AddCacheOrchestratorEdge(
            builder.Configuration,
            edge => edge.AddVarnish());
        using IHost host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        using var client = new HttpClient { BaseAddress = varnish.Address };
        using HttpResponseMessage first = await client.GetAsync("/item", TestContext.Current.CancellationToken);
        using HttpResponseMessage second = await client.GetAsync("/item", TestContext.Current.CancellationToken);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        first.Headers.GetValues("Cache-Status").Should().ContainSingle("Varnish; fwd=uri-miss");
        second.Headers.GetValues("Cache-Status").Should().ContainSingle("Varnish; hit");
        first.Headers.Contains("xkey").Should().BeFalse();
        first.Headers.Contains("X-CacheOrchestrator-Edge-Ttl").Should().BeFalse();

        IEdgeInvalidationProvider provider = host.Services
            .GetServices<IEdgeInvalidationProvider>()
            .Single(candidate => candidate.Name == "Varnish");
        EdgeInvalidationResult invalidation = await provider.InvalidateAsync(
            new EdgeInvalidationRequest
            {
                InstanceName = "edge",
                Tags = ["coe1-integration-item"]
            },
            TestContext.Current.CancellationToken);

        invalidation.Succeeded.Should().BeTrue();
        using HttpResponseMessage afterInvalidation = await client.GetAsync(
            "/item",
            TestContext.Current.CancellationToken);
        afterInvalidation.EnsureSuccessStatusCode();
        afterInvalidation.Headers.GetValues("Cache-Status").Should()
            .ContainSingle("Varnish; fwd=uri-miss");

        await host.StopAsync(TestContext.Current.CancellationToken);
    }
}
