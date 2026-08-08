using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Behavior;

public class ConfigBehaviorTests
{
    // -------------------------------------------------------------------------
    // Version – changing version forces new Fusion cache key (MISS)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Version_Change_ForcesFusionMiss()
    {
        int factoryCalls = 0;

        async Task<(string value, int calls)> RunOnceAsync(string version)
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cache:OutputCache:Provider"] = "InMemory",
                    ["Cache:FusionCache:Provider"] = "InMemory",
                    ["Cache:Domains:ver:FusionCacheSoftTtlSeconds"] = "300",
                    ["Cache:Domains:ver:Version"] = version
                })
                .Build();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddCacheOrchestrator(config);
            await using ServiceProvider sp = services.BuildServiceProvider();

            IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
            IDomainCacheOptionsProvider domains = sp.GetRequiredService<IDomainCacheOptionsProvider>();

            DefaultHttpContext http = new();
            http.Request.Method = "GET";
            http.Request.Path = "/api/ver-item";
            domains.EnsureDomainOptions(http, "ver");

            string value = await cache.GetOrSetAsync(http, _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult($"data-{version}");
            }, TestContext.Current.CancellationToken);

            // warm second call (same provider / same version) should hit
            await cache.GetOrSetAsync(http, _ =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult("should-not-run");
            }, TestContext.Current.CancellationToken);

            return (value, factoryCalls);
        }

        factoryCalls = 0;
        (string? v1, int callsAfterV1) = await RunOnceAsync("2026-08-01T10:00:00Z");
        v1.Should().Be("data-2026-08-01T10:00:00Z");
        callsAfterV1.Should().Be(1); // miss + hit

        // New Version ? new key ? factory again
        (string? v2, int callsAfterV2) = await RunOnceAsync("2026-08-01T11:00:00Z");
        v2.Should().Be("data-2026-08-01T11:00:00Z");
        callsAfterV2.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // OutputCacheEnabled = false ? endpoint always runs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OutputCacheEnabled_False_AlwaysExecutesEndpoint()
    {
        int[] hits = new int[1];

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCache:Provider"] = "InMemory",
                ["Cache:Domains:nocache:OutputCacheEnabled"] = "false",
                ["Cache:Domains:nocache:Version"] = "v1"
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config);

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        app.MapGet("/nocache", () =>
        {
            Interlocked.Increment(ref hits[0]);
            return Results.Text("body");
        })
        .CacheOutputWithDomain("nocache");

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            (await client.GetAsync("/nocache", TestContext.Current.CancellationToken)).IsSuccessStatusCode.Should().BeTrue();
            (await client.GetAsync("/nocache", TestContext.Current.CancellationToken)).IsSuccessStatusCode.Should().BeTrue();
            Volatile.Read(ref hits[0]).Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // RespectNoStore – Fusion skips cache when request has Cache-Control: no-store
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RespectNoStore_SkipsFusionCache()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCache:Provider"] = "InMemory",
                ["Cache:Domains:nostore:FusionCacheRespectNoStore"] = "true",
                ["Cache:Domains:nostore:FusionCacheSoftTtlSeconds"] = "300",
                ["Cache:Domains:nostore:Version"] = "v1"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IDomainCacheOptionsProvider domains = sp.GetRequiredService<IDomainCacheOptionsProvider>();

        DefaultHttpContext http = new();
        http.Request.Method = "GET";
        http.Request.Path = "/api/nostore";
        http.Request.Headers.CacheControl = "no-store";
        domains.EnsureDomainOptions(http, "nostore");

        int calls = 0;

        await cache.GetOrSetAsync(http, _ =>
        {
            calls++;
            return Task.FromResult(1);
        }, TestContext.Current.CancellationToken);

        await cache.GetOrSetAsync(http, _ =>
        {
            calls++;
            return Task.FromResult(2);
        }, TestContext.Current.CancellationToken);

        calls.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // ClientCacheControlHeader written on successful cached response path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClientCacheHeader_IsWrittenOnResponse_FromTtlSettings()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCache:Provider"] = "InMemory",
                ["Cache:Domains:hdr:OutputCacheTtlSeconds"] = "60",
                ["Cache:Domains:hdr:ClientCacheability"] = "Public",
                ["Cache:Domains:hdr:ClientTtlSeconds"] = "42",
                ["Cache:Domains:hdr:ClientTtlMinSeconds"] = "42",
                ["Cache:Domains:hdr:Version"] = "v1"
                // no ScheduledUpdateUtc ? max-age = ClientTtlSeconds
            })
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config);

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        app.MapGet("/hdr", () => Results.Text("ok"))
           .CacheOutputWithDomain("hdr");

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            HttpResponseMessage response = await client.GetAsync("/hdr", TestContext.Current.CancellationToken);
            response.IsSuccessStatusCode.Should().BeTrue();

            response.Headers.TryGetValues("Cache-Control", out IEnumerable<string>? values).Should().BeTrue();
            string cacheControl = string.Join(",", values!);

            cacheControl.Should().Contain("max-age=42");
            cacheControl.Should().Contain("public");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // DomainDefaults fallback when domain is not listed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DomainDefaults_Apply_WhenDomainMissing()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCache:Provider"] = "InMemory",
                ["Cache:DomainDefaults:FusionCacheSoftTtlSeconds"] = "123",
                ["Cache:DomainDefaults:OutputCacheTtlSeconds"] = "45",
                ["Cache:DomainDefaults:ClientCacheability"] = "Public",
                ["Cache:DomainDefaults:ClientTtlSeconds"] = "45",
                ["Cache:DomainDefaults:ClientTtlMinSeconds"] = "15"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainCacheOptionsProvider domains = sp.GetRequiredService<IDomainCacheOptionsProvider>();
        DefaultHttpContext http = new();

        DomainCacheOptions cfg = domains.EnsureDomainOptions(http, "unknown-domain-xyz");

        cfg.Should().NotBeNull();
        cfg.Domain.Should().Be("unknown-domain-xyz");
        cfg.FusionCacheSoftTtl.Should().Be(TimeSpan.FromSeconds(123));
        cfg.OutputTtl.Should().Be(TimeSpan.FromSeconds(45));
        cfg.ClientCacheability.Should().Be(ClientCacheability.Public);
        cfg.ClientTtlSeconds.Should().Be(45);
        cfg.ClientTtlMinSeconds.Should().Be(15);
    }
}