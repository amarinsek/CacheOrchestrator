using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.OutputCaching;

[Collection("Redis")]
public class OutputCacheRedisTests
{
    private readonly RedisFixture _redis;

    public OutputCacheRedisTests(RedisFixture redis)
    {
        _redis = redis;
    }

    private sealed class HitCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private async Task<(HttpClient client, WebApplication app, string basePath)> StartAsync(string domain)
    {
        string basePath = "/" + domain;

        Dictionary<string, string?> configValues = new()
        {
            ["Cache:OutputCache:Provider"] = "Redis",
            ["Cache:FusionCache:Provider"] = "Redis",
            ["Cache:Redis:Configuration"] = _redis.ConnectionString,
            [$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = "60",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCacheControlHeader"] = "public, max-age=60"
        };

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(config);
        builder.Services.AddCacheOrchestratorAspNetCore(config, o => o.AddRedisBackend());
        builder.Services.AddCacheOrchestratorFusionCache(config);
        builder.Services.AddSingleton<HitCounter>();

        WebApplication app = builder.Build();

        app.UseRouting();
        app.UseCacheOrchestrator();

        app.MapGet(basePath, (HitCounter hits) =>
        {
            hits.Increment();
            return Results.Text("products-body");
        })
        .CacheOutputWithDomain(domain);

        app.MapGet(basePath + "/{id}", (string id, HitCounter hits) =>
        {
            hits.Increment();
            return Results.Text($"product-{id}");
        })
        .CacheOutputWithDomain(domain);

        await app.StartAsync();
        return (app.GetTestClient(), app, basePath);
    }

    [Fact]
    public async Task Get_SecondRequest_IsServedFromCache()
    {
        string domain = "oc-hit-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app, string? basePath) = await StartAsync(domain);
        try
        {
            HttpResponseMessage r1 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            string body1 = await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            r1.IsSuccessStatusCode.Should().BeTrue($"status={(int)r1.StatusCode}, body={body1}");
            body1.Should().Be("products-body");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            HttpResponseMessage r2 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            string body2 = await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            r2.IsSuccessStatusCode.Should().BeTrue();
            body2.Should().Be("products-body");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_AfterInvalidateDomain_IsMissAgain()
    {
        string domain = "oc-inv-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app, string? basePath) = await StartAsync(domain);
        try
        {
            HttpResponseMessage r1 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            r1.IsSuccessStatusCode.Should().BeTrue();
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            ICacheOrchestratorInvalidator invalidator = app.Services.GetRequiredService<ICacheOrchestratorInvalidator>();
            await invalidator.InvalidateDomainAsync(domain, TestContext.Current.CancellationToken);

            HttpResponseMessage r2 = await client.GetAsync(basePath, TestContext.Current.CancellationToken);
            r2.IsSuccessStatusCode.Should().BeTrue();
            (await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("products-body");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_DifferentIds_AreIndependent()
    {
        string domain = "oc-ids-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app, string? basePath) = await StartAsync(domain);
        try
        {
            string a = await (await client.GetAsync(basePath + "/1", TestContext.Current.CancellationToken))
                .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            string b = await (await client.GetAsync(basePath + "/2", TestContext.Current.CancellationToken))
                .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            a.Should().Be("product-1");
            b.Should().Be("product-2");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}