using CacheOrchestrator.Edge.Cloudflare;
using CacheOrchestrator.Edge.DependencyInjection;
using CacheOrchestrator.Edge.Tags;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Entity;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Edge;

public class CloudflareOutputCacheHttpTests
{
    [Fact]
    public async Task OutputCacheHit_ReplaysOriginalCloudflareTags()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Namespace"] = "edge-test",
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EdgeInstances:edge:Provider"] = "Cloudflare",
            ["Cache:EdgeInstances:edge:Cloudflare:ZoneId"] = "zone-1",
            ["Cache:EdgeInstances:edge:Cloudflare:ApiToken"] = "token-1",
            ["Cache:Domains:catalog:OutputCache:Enabled"] = "true",
            ["Cache:Domains:catalog:OutputCache:TtlSeconds"] = "60",
            ["Cache:Domains:catalog:DataCache:Enabled"] = "true",
            ["Cache:Domains:catalog:DataCache:TtlSeconds"] = "60",
            ["Cache:Domains:catalog:ClientCache:Cacheability"] = "Public",
            ["Cache:Domains:catalog:Edge:Enabled"] = "true",
            ["Cache:Domains:catalog:Edge:Instance"] = "edge",
            ["Cache:Domains:catalog:Edge:TtlSeconds"] = "600"
        });
        builder.Services.AddCacheOrchestrator(builder.Configuration);
        builder.Services.AddCacheOrchestratorEdge(
            builder.Configuration,
            edge => edge.AddCloudflare());
        var calls = new RequestCounter();
        builder.Services.AddSingleton(calls);

        await using WebApplication app = builder.Build();
        app.UseCacheOrchestrator();
        app.MapGet("/products/{id:int}", async (
            HttpContext http,
            int id,
            IDomainDataCache cache,
            RequestCounter counter) =>
        {
            var value = await cache.GetOrSetEntityAsync(
                http,
                _ => Task.FromResult(
                    EntityCache.Create(new { id, call = counter.Increment() })
                        .DependsOn("categories", 7)),
                http.RequestAborted);
            return Results.Json(value);
        })
            .CacheOutputWithDomain("catalog", resourceRouteKey: "id", entityKind: "products");
        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        using HttpResponseMessage first = await client.GetAsync(
            "/products/42",
            TestContext.Current.CancellationToken);
        using HttpResponseMessage second = await client.GetAsync(
            "/products/42",
            TestContext.Current.CancellationToken);

        string firstTags = string.Join(',', first.Headers.GetValues("Cache-Tag"));
        string secondTags = string.Join(',', second.Headers.GetValues("Cache-Tag"));
        firstTags.Should().NotBeEmpty().And.Contain("coe1-");
        firstTags.Should().Contain(
            new EdgeTagProjector().Project("edge-test-edge-edge", "entity:catalog:categories:7"));
        secondTags.Should().Be(firstTags);
        first.Headers.Contains("X-CacheOrchestrator-Staged-Tags").Should().BeFalse();
        second.Headers.Contains("X-CacheOrchestrator-Staged-Tags").Should().BeFalse();
        second.Headers.GetValues("X-CacheOrchestrator").Single().Should().Contain("oc=hit");
        calls.Value.Should().Be(1);
    }

    private sealed class RequestCounter
    {
        private int _value;

        public int Value => _value;

        public int Increment() => Interlocked.Increment(ref _value);
    }
}
