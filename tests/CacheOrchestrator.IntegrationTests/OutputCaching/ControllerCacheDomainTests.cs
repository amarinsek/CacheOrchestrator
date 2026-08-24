using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.OutputCaching;

public sealed class ControllerHitCounter
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public void Increment() => Interlocked.Increment(ref _count);
}

[CacheDomain("controller-products")]
public sealed class ProductsTestController : ControllerBase
{
    private readonly ControllerHitCounter _hits;

    public ProductsTestController(ControllerHitCounter hits)
    {
        _hits = hits;
    }

    [HttpGet("/ctrl/products")]
    public IActionResult GetAll()
    {
        _hits.Increment();
        return Content("ctrl-products-body", "text/plain");
    }

    [HttpGet("/ctrl/products/{id}")]
    [CacheDomain("controller-product-detail")]
    public IActionResult GetById(string id)
    {
        _hits.Increment();
        return Content($"ctrl-product-{id}", "text/plain");
    }
}

public class ControllerCacheDomainTests
{
    private async Task<(HttpClient client, WebApplication app)> StartAsync()
    {
        string domain = "controller-products";
        string detailDomain = "controller-product-detail";

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCache:Provider"] = "InMemory",
                [$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = "60",
                [$"Cache:Domains:{domain}:Version"] = "v1",
                [$"Cache:Domains:{detailDomain}:OutputCache:TtlSeconds"] = "60",
                [$"Cache:Domains:{detailDomain}:Version"] = "v1"
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestratorAspNetCore(config);
        builder.Services.AddCacheOrchestratorFusionCache(config);
        builder.Services.AddSingleton<ControllerHitCounter>();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ProductsTestController).Assembly);

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapControllers();

        await app.StartAsync();
        return (app.GetTestClient(), app);
    }

    [Fact]
    public async Task Controller_Get_SecondRequest_IsServedFromCache()
    {
        (HttpClient? client, WebApplication? app) = await StartAsync();
        try
        {
            HttpResponseMessage r1 = await client.GetAsync("/ctrl/products", TestContext.Current.CancellationToken);
            string b1 = await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            r1.IsSuccessStatusCode.Should().BeTrue($"status={(int)r1.StatusCode}, body={b1}");
            b1.Should().Be("ctrl-products-body");
            app.Services.GetRequiredService<ControllerHitCounter>().Count.Should().Be(1);

            HttpResponseMessage r2 = await client.GetAsync("/ctrl/products", TestContext.Current.CancellationToken);
            (await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("ctrl-products-body");
            app.Services.GetRequiredService<ControllerHitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Controller_ActionLevelAttribute_UsesSeparateRoutes()
    {
        (HttpClient? client, WebApplication? app) = await StartAsync();
        try
        {
            string a = await (await client.GetAsync("/ctrl/products/1", TestContext.Current.CancellationToken))
                .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            string b = await (await client.GetAsync("/ctrl/products/2", TestContext.Current.CancellationToken))
                .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            a.Should().Be("ctrl-product-1");
            b.Should().Be("ctrl-product-2");
            app.Services.GetRequiredService<ControllerHitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
