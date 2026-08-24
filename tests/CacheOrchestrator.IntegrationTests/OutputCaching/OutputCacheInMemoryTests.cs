using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CacheOrchestrator.IntegrationTests.OutputCaching;

public class OutputCacheInMemoryTests
{
    private static async Task<(HttpClient client, IHost host)> CreateClientAsync()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCache:Provider"] = "InMemory",
                ["Cache:Domains:products:OutputCache:Ttl"] = "00:01:00",
                ["Cache:Domains:products:Version"] = "v1",
                ["Cache:Domains:products:ClientCacheControlHeader"] = "public, max-age=60"
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(config);
        builder.Services.AddCacheOrchestrator(config);

        WebApplication app = builder.Build();
        app.UseCacheOrchestrator();

        app.MapGet("/products", () => Results.Content("products-body", "text/plain"))
           .CacheOutputWithDomain("products");

        app.MapPost("/products", () => Results.Content("created", "text/plain"))
           .CacheOutputWithDomain("products");

        app.MapGet("/products/{id}", (string id) => Results.Content($"product-{id}", "text/plain"))
           .CacheOutputWithDomain("products");

        await app.StartAsync();

        HttpClient client = app.GetTestClient();
        return (client, app);
    }

    [Fact]
    public async Task Get_SecondRequest_ReturnsCachedBody()
    {
        (HttpClient? client, IHost? host) = await CreateClientAsync();
        using (host)
        {
            HttpResponseMessage r1 = await client.GetAsync("/products", TestContext.Current.CancellationToken);
            string b1 = await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            HttpResponseMessage r2 = await client.GetAsync("/products", TestContext.Current.CancellationToken);
            string b2 = await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            r1.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            r2.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            b1.Should().Be("products-body");
            b2.Should().Be("products-body");
        }
    }

    [Fact]
    public async Task Get_DifferentIds_AreIndependent()
    {
        (HttpClient? client, IHost? host) = await CreateClientAsync();
        using (host)
        {
            string a = await (await client.GetAsync("/products/1", TestContext.Current.CancellationToken))
                .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            string b = await (await client.GetAsync("/products/2", TestContext.Current.CancellationToken))
                .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            a.Should().Be("product-1");
            b.Should().Be("product-2");
        }
    }

    [Fact]
    public async Task Post_IsNotCached_LikeGet()
    {
        (HttpClient? client, IHost? host) = await CreateClientAsync();
        using (host)
        {
            HttpResponseMessage post = await client.PostAsync("/products", null, TestContext.Current.CancellationToken);
            string postBody = await post.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            HttpResponseMessage get = await client.GetAsync("/products", TestContext.Current.CancellationToken);
            string getBody = await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            postBody.Should().Be("created");
            getBody.Should().Be("products-body");
        }
    }

    [Fact]
    public async Task Get_WithTrackingQuery_SharesCacheWithCleanUrl()
    {
        (HttpClient? client, IHost? host) = await CreateClientAsync();
        using (host)
        {
            HttpResponseMessage r1 = await client.GetAsync("/products?utm_source=google", TestContext.Current.CancellationToken);
            string b1 = await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            HttpResponseMessage r2 = await client.GetAsync("/products", TestContext.Current.CancellationToken);
            string b2 = await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            b1.Should().Be("products-body");
            b2.Should().Be("products-body");
        }
    }
}