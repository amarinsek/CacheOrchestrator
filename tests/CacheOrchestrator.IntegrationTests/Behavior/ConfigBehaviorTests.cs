using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.IntegrationTests.Behavior;

public class ConfigBehaviorTests
{
    // -------------------------------------------------------------------------
    // Version cutover (HTTP / Output Cache): v1 miss → hit → reload v2 → miss
    // -------------------------------------------------------------------------

    /// <summary>
    /// Live domain Version change on a running host must change the Output Cache key
    /// (<c>data-version</c> vary) so the next request is an OC miss, not a stale v1 hit.
    /// </summary>
    [Fact]
    public async Task Version_Change_OnRunningHost_ForcesOutputCacheMiss()
    {
        string domain = "ver-oc-" + Guid.NewGuid().ToString("N");
        int handlerCalls = 0;

        var initial = new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientCache:Ttl"] = "00:01:00",
            [$"Cache:Domains:{domain}:ClientCache:TtlMin"] = "00:01:00",
            [$"Cache:Domains:{domain}:OutputCache:Ttl"] = "00:05:00",
            [$"Cache:Domains:{domain}:DataCache:Ttl"] = "00:05:00",
        };

        var reloadSource = new ReloadableMemoryConfigurationSource(initial);
        IConfigurationRoot config = new ConfigurationBuilder()
            .Add(reloadSource)
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

        app.MapGet("/ver", () =>
        {
            Interlocked.Increment(ref handlerCalls);
            return Results.Text("body-v1-generation");
        })
        .CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            // --- Generation v1: miss then hit ---
            HttpResponseMessage r1 = await client.GetAsync("/ver", TestContext.Current.CancellationToken);
            r1.IsSuccessStatusCode.Should().BeTrue();
            (await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("body-v1-generation");
            GetXCache(r1).Should().Contain("oc=miss");
            GetXCache(r1).Should().Contain("version=v1");
            Volatile.Read(ref handlerCalls).Should().Be(1);

            HttpResponseMessage r2 = await client.GetAsync("/ver", TestContext.Current.CancellationToken);
            r2.IsSuccessStatusCode.Should().BeTrue();
            GetXCache(r2).Should().Contain("oc=hit");
            GetXCache(r2).Should().Contain("version=v1");
            Volatile.Read(ref handlerCalls).Should().Be(1, "Output Cache hit must not re-run the endpoint");

            // --- Live config reload: Version v1 → v2 (same process, IOptionsMonitor path) ---
            reloadSource.Provider.Should().NotBeNull();
            reloadSource.Provider!.SetAndReload($"Cache:Domains:{domain}:Version", "v2");

            await WaitForDomainVersionAsync(app.Services, domain, expectedVersion: "v2");

            // --- Generation v2: must miss (new data-version vary key), then hit ---
            HttpResponseMessage r3 = await client.GetAsync("/ver", TestContext.Current.CancellationToken);
            r3.IsSuccessStatusCode.Should().BeTrue();
            GetXCache(r3).Should().Contain("oc=miss",
                "Version bump must change OC key so the v1 entry is not served");
            GetXCache(r3).Should().Contain("version=v2");
            Volatile.Read(ref handlerCalls).Should().Be(2, "OC miss after Version cutover must execute the endpoint again");

            HttpResponseMessage r4 = await client.GetAsync("/ver", TestContext.Current.CancellationToken);
            r4.IsSuccessStatusCode.Should().BeTrue();
            GetXCache(r4).Should().Contain("oc=hit");
            GetXCache(r4).Should().Contain("version=v2");
            Volatile.Read(ref handlerCalls).Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    private static string GetXCache(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Cache", out IEnumerable<string>? values)
            ? string.Join(",", values)
            : string.Empty;

    /// <summary>
    /// Options reload is token-driven; poll until the domain snapshot reflects the new Version
    /// (or fail fast if the monitor never updates).
    /// </summary>
    private static async Task WaitForDomainVersionAsync(
        IServiceProvider services,
        string domain,
        string expectedVersion)
    {
        IRequestDomainCacheOptions domains = services.GetRequiredService<IRequestDomainCacheOptions>();
        IOptionsMonitor<CacheOrchestratorOptions> monitor =
            services.GetRequiredService<IOptionsMonitor<CacheOrchestratorOptions>>();

        // Touch CurrentValue so bound options re-evaluate against the reloaded configuration.
        _ = monitor.CurrentValue;

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            DomainCacheOptions snap = domains.GetOrCreateDomainOptions(domain);
            if (string.Equals(snap.Version, expectedVersion, StringComparison.Ordinal))
                return;

            await Task.Delay(20, TestContext.Current.CancellationToken);
            _ = monitor.CurrentValue;
        }

        DomainCacheOptions final = domains.GetOrCreateDomainOptions(domain);
        final.Version.Should().Be(expectedVersion,
            "IOptionsMonitor / DomainCacheOptionsProvider must pick up reloaded Version before asserting OC behaviour");
    }

    // -------------------------------------------------------------------------
    // Version — changing version forces new Fusion cache key (MISS)
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
                    ["Cache:Domains:ver:DataCache:Ttl"] = "00:05:00",
                    ["Cache:Domains:ver:Version"] = version
                })
                .Build();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddCacheOrchestrator(config);
            await using ServiceProvider sp = services.BuildServiceProvider();

            IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
            IRequestDomainCacheOptions domains = sp.GetRequiredService<IRequestDomainCacheOptions>();

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
                ["Cache:Domains:nocache:OutputCache:Enabled"] = "false",
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
    // RespectNoStore � Fusion skips cache when request has Cache-Control: no-store
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RespectNoStore_SkipsFusionCache()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCache:Provider"] = "InMemory",
                ["Cache:Domains:nostore:FusionCache:RespectNoStore"] = "true",
                ["Cache:Domains:nostore:DataCache:Ttl"] = "00:05:00",
                ["Cache:Domains:nostore:Version"] = "v1"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IRequestDomainCacheOptions domains = sp.GetRequiredService<IRequestDomainCacheOptions>();

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
                ["Cache:Domains:hdr:OutputCache:Ttl"] = "00:01:00",
                ["Cache:Domains:hdr:ClientCache:Cacheability"] = "Public",
                ["Cache:Domains:hdr:ClientCache:Ttl"] = "00:00:42",
                ["Cache:Domains:hdr:ClientCache:TtlMin"] = "00:00:42",
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
                ["Cache:DomainDefaults:DataCache:Ttl"] = "00:02:03",
                ["Cache:DomainDefaults:OutputCache:Ttl"] = "00:00:45",
                ["Cache:DomainDefaults:ClientCache:Cacheability"] = "Public",
                ["Cache:DomainDefaults:ClientCache:Ttl"] = "00:00:45",
                ["Cache:DomainDefaults:ClientCache:TtlMin"] = "00:00:15"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IRequestDomainCacheOptions domains = sp.GetRequiredService<IRequestDomainCacheOptions>();
        DefaultHttpContext http = new();

        DomainCacheOptions cfg = domains.EnsureDomainOptions(http, "unknown-domain-xyz");

        cfg.Should().NotBeNull();
        cfg.Domain.Should().Be("unknown-domain-xyz");
        cfg.DataCacheTtl.Should().Be(TimeSpan.FromSeconds(123));
        cfg.OutputTtl.Should().Be(TimeSpan.FromSeconds(45));
        cfg.ClientCacheability.Should().Be(ClientCacheability.Public);
        cfg.ClientTtlSeconds.Should().Be(45);
        cfg.ClientTtlMinSeconds.Should().Be(15);
    }
}