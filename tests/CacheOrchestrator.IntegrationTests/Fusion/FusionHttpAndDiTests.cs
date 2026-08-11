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

namespace CacheOrchestrator.IntegrationTests.Fusion;

/// <summary>
/// FusionCache integration scenarios from the B backlog:
/// HTTP happy path without manual Ensure, unresolved domain, explicit domain overload,
/// live Version reload → FC miss, fail-safe stale over HTTP, encoding vary, Fusion off.
/// </summary>
public class FusionHttpAndDiTests
{
    private sealed class FactoryCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private static Dictionary<string, string?> DomainBase(string domain, Action<Dictionary<string, string?>>? extra = null)
    {
        Dictionary<string, string?> d = new()
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientTtlSeconds"] = "60",
            [$"Cache:Domains:{domain}:ClientTtlMinSeconds"] = "60",
            [$"Cache:Domains:{domain}:OutputCacheTtlSeconds"] = "120",
            [$"Cache:Domains:{domain}:FusionCacheSoftTtlSeconds"] = "300",
            [$"Cache:Domains:{domain}:FusionCacheJitterSeconds"] = "0",
            [$"Cache:Domains:{domain}:FusionCacheEagerRefreshRatio"] = "0",
        };
        extra?.Invoke(d);
        return d;
    }

    private static async Task<(HttpClient Client, WebApplication App)> StartHttpAsync(
        Dictionary<string, string?> configValues,
        Action<WebApplication> map)
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config);
        builder.Services.AddSingleton<FactoryCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        map(app);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return (app.GetTestClient(), app);
    }

    private static async Task<(HttpResponseMessage Res, string XCache, string Body)> GetAsync(
        HttpClient client,
        string url,
        Dictionary<string, string>? headers = null)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, url);
        if (headers is not null)
        {
            foreach (KeyValuePair<string, string> h in headers)
                req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        HttpResponseMessage res = await client.SendAsync(req, TestContext.Current.CancellationToken);
        string body = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string xCache = res.Headers.TryGetValues("X-Cache", out IEnumerable<string>? values)
            ? string.Join(",", values)
            : string.Empty;
        return (res, xCache, body);
    }

    private static DefaultHttpContext CreateHttp(string path = "/api/item")
    {
        DefaultHttpContext http = new();
        http.Request.Method = "GET";
        http.Request.Path = path;
        return http;
    }

    private static CacheDisposition? Disposition(HttpContext http) =>
        http.Items[CacheOrchestratorKeys.DispositionKey] as CacheDisposition;

    // =========================================================================
    // B13 / B15 — Happy path: domain from OC policy metadata, no EnsureDomainOptions
    // =========================================================================

    [Fact]
    public async Task HappyPath_DomainFromEndpointPolicy_NoManualEnsure_CachesWithFusionAndOutput()
    {
        string domain = "fc-happy-" + Guid.NewGuid().ToString("N");

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(DomainBase(domain), a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainFusionCache cache, FactoryCounter factory) =>
            {
                // Product happy path: domain already set by DomainOutputCachePolicy — no EnsureDomainOptions.
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    factory.Increment();
                    return Task.FromResult("happy-payload");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            r1.IsSuccessStatusCode.Should().BeTrue();
            b1.Should().Be("happy-payload");
            x1.Should().Contain($"domain={domain}");
            x1.Should().Contain("output=miss");
            x1.Should().Contain("data=miss");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            b2.Should().Be("happy-payload");
            x2.Should().Contain("output=hit");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1,
                "second request must be served from Output Cache without re-running Fusion factory");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task HappyPath_DomainFromPolicy_WhenOutputDisabled_FusionHitsWithoutEnsure()
    {
        string domain = "fc-meta-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = DomainBase(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCacheEnabled"] = "false";
        });

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(config, a =>
        {
            // Policy still attaches domain metadata even when OC is disabled for the domain.
            a.MapGet("/x", async (HttpContext http, IDomainFusionCache cache, FactoryCounter factory) =>
            {
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    factory.Increment();
                    return Task.FromResult("from-meta");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            b1.Should().Be("from-meta");
            x1.Should().Contain("output=bypass");
            x1.Should().Contain("data=miss");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            b2.Should().Be("from-meta");
            x2.Should().Contain("data=hit");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // B14 — No domain resolved → factory uncached (DI)
    // =========================================================================

    [Fact]
    public async Task GetOrSetAsync_WhenNoDomainResolved_RunsFactoryUncached_AndSetsUnresolved()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        int factoryCalls = 0;

        DefaultHttpContext http1 = CreateHttp("/api/orphan");
        string v1 = await cache.GetOrSetAsync(http1, _ =>
        {
            factoryCalls++;
            return Task.FromResult("a");
        }, TestContext.Current.CancellationToken);

        DefaultHttpContext http2 = CreateHttp("/api/orphan");
        string v2 = await cache.GetOrSetAsync(http2, _ =>
        {
            factoryCalls++;
            return Task.FromResult("b");
        }, TestContext.Current.CancellationToken);

        v1.Should().Be("a");
        v2.Should().Be("b");
        factoryCalls.Should().Be(2, "without a domain every call must run the factory");
        Disposition(http1)!.Data.Should().Be(DataCacheResult.Unresolved);
        Disposition(http2)!.Data.Should().Be(DataCacheResult.Unresolved);
    }

    // =========================================================================
    // B16 — Explicit domain overload (Fusion-only, DI)
    // =========================================================================

    [Fact]
    public async Task GetOrSetAsync_WithExplicitDomain_CachesWithoutEnsureOrEndpointMetadata()
    {
        string domain = "fc-explicit";
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
                [$"Cache:Domains:{domain}:Version"] = "v1",
                [$"Cache:Domains:{domain}:FusionCacheSoftTtlSeconds"] = "300",
                [$"Cache:Domains:{domain}:FusionCacheJitterSeconds"] = "0",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        int factoryCalls = 0;

        DefaultHttpContext http = CreateHttp("/api/explicit");
        // No EnsureDomainOptions, no endpoint metadata — only domain overload.
        string first = await cache.GetOrSetAsync(http, domain, _ =>
        {
            factoryCalls++;
            return Task.FromResult("explicit-value");
        }, TestContext.Current.CancellationToken);

        string second = await cache.GetOrSetAsync(http, domain, _ =>
        {
            factoryCalls++;
            return Task.FromResult("should-not-run");
        }, TestContext.Current.CancellationToken);

        first.Should().Be("explicit-value");
        second.Should().Be("explicit-value");
        factoryCalls.Should().Be(1);
        Disposition(http)!.Data.Should().Be(DataCacheResult.Hit);
    }

    // =========================================================================
    // B17 — Live Version reload → Fusion miss on same host
    // =========================================================================

    [Fact]
    public async Task Version_Reload_OnRunningHost_ForcesFusionMiss()
    {
        string domain = "fc-ver-" + Guid.NewGuid().ToString("N");
        var initial = new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:OutputCacheEnabled"] = "false",
            [$"Cache:Domains:{domain}:ClientCacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientTtlSeconds"] = "60",
            [$"Cache:Domains:{domain}:ClientTtlMinSeconds"] = "60",
            [$"Cache:Domains:{domain}:FusionCacheSoftTtlSeconds"] = "300",
            [$"Cache:Domains:{domain}:FusionCacheJitterSeconds"] = "0",
            [$"Cache:Domains:{domain}:FusionCacheEagerRefreshRatio"] = "0",
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
        builder.Services.AddSingleton<FactoryCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        app.MapGet("/x", async (HttpContext http, IDomainFusionCache cache, FactoryCounter factory) =>
        {
            string value = await cache.GetOrSetAsync(http, _ =>
            {
                factory.Increment();
                return Task.FromResult("gen-" + factory.Count);
            }, http.RequestAborted);
            return Results.Text(value);
        }).CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            b1.Should().Be("gen-1");
            x1.Should().Contain("data=miss");
            x1.Should().Contain("version=v1");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            b2.Should().Be("gen-1");
            x2.Should().Contain("data=hit");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            reloadSource.Provider.Should().NotBeNull();
            reloadSource.Provider!.SetAndReload($"Cache:Domains:{domain}:Version", "v2");
            await WaitForDomainVersionAsync(app.Services, domain, "v2");

            (HttpResponseMessage r3, string x3, string b3) = await GetAsync(client, "/x");
            b3.Should().Be("gen-2");
            x3.Should().Contain("data=miss", "Version bump must change Fusion key space");
            x3.Should().Contain("version=v2");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(2);

            (HttpResponseMessage r4, string x4, string b4) = await GetAsync(client, "/x");
            b4.Should().Be("gen-2");
            x4.Should().Contain("data=hit");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    private static async Task WaitForDomainVersionAsync(
        IServiceProvider services,
        string domain,
        string expectedVersion)
    {
        IDomainCacheOptionsProvider domains = services.GetRequiredService<IDomainCacheOptionsProvider>();
        IOptionsMonitor<CacheOrchestratorOptions> monitor =
            services.GetRequiredService<IOptionsMonitor<CacheOrchestratorOptions>>();

        _ = monitor.CurrentValue;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (string.Equals(domains.GetOrCreateDomainOptions(domain).Version, expectedVersion, StringComparison.Ordinal))
                return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
            _ = monitor.CurrentValue;
        }

        domains.GetOrCreateDomainOptions(domain).Version.Should().Be(expectedVersion);
    }

    // =========================================================================
    // B18 — Fail-safe stale over HTTP + X-Cache data=stale
    // =========================================================================

    [Fact]
    public async Task FailSafe_AfterSoftExpiry_HttpReturnsStale_AndXCacheDataStale()
    {
        string domain = "fc-stale-" + Guid.NewGuid().ToString("N");
        int[] phase = [0];

        Dictionary<string, string?> config = DomainBase(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCacheEnabled"] = "false";
            d[$"Cache:Domains:{domain}:FusionCacheSoftTtlSeconds"] = "1";
            d[$"Cache:Domains:{domain}:FusionCacheHardTtlSeconds"] = "3600";
            d[$"Cache:Domains:{domain}:FusionCacheFailSafeSeconds"] = "86400";
            d[$"Cache:Domains:{domain}:FusionCacheJitterSeconds"] = "0";
            d[$"Cache:Domains:{domain}:FusionCacheEagerRefreshRatio"] = "0";
            d[$"Cache:Domains:{domain}:FusionCacheFactorySoftTimeoutSeconds"] = "5";
            d[$"Cache:Domains:{domain}:FusionCacheFactoryHardTimeoutSeconds"] = "10";
        });

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(config, a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainFusionCache cache) =>
            {
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    int n = Interlocked.Increment(ref phase[0]);
                    if (n == 1)
                        return Task.FromResult("good");
                    throw new InvalidOperationException("upstream down");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            r1.IsSuccessStatusCode.Should().BeTrue();
            b1.Should().Be("good");
            x1.Should().Contain("data=miss");

            await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            r2.IsSuccessStatusCode.Should().BeTrue();
            b2.Should().Be("good", "fail-safe must return last good value");
            x2.Should().Contain("data=stale");
            Volatile.Read(ref phase[0]).Should().Be(2, "factory must re-run after soft expiry");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // B19 — Encoding vary on Fusion keys (DI)
    // =========================================================================

    [Fact]
    public async Task FusionVaryOnEncoding_DifferentAcceptEncoding_AreIndependentEntries()
    {
        string domain = "fc-enc";
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
                [$"Cache:Domains:{domain}:Version"] = "v1",
                [$"Cache:Domains:{domain}:FusionCacheSoftTtlSeconds"] = "300",
                [$"Cache:Domains:{domain}:FusionCacheVaryOnEncoding"] = "true",
                [$"Cache:Domains:{domain}:FusionCacheVaryOnPublicAddress"] = "false",
                [$"Cache:Domains:{domain}:FusionCacheJitterSeconds"] = "0",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IDomainCacheOptionsProvider domains = sp.GetRequiredService<IDomainCacheOptionsProvider>();
        int factoryCalls = 0;

        DefaultHttpContext gzip = CreateHttp("/api/enc");
        gzip.Request.Headers.AcceptEncoding = "gzip";
        domains.EnsureDomainOptions(gzip, domain);

        string g1 = await cache.GetOrSetAsync(gzip, _ =>
        {
            factoryCalls++;
            return Task.FromResult("gzip-body");
        }, TestContext.Current.CancellationToken);

        DefaultHttpContext br = CreateHttp("/api/enc");
        br.Request.Headers.AcceptEncoding = "br";
        domains.EnsureDomainOptions(br, domain);

        string b1 = await cache.GetOrSetAsync(br, _ =>
        {
            factoryCalls++;
            return Task.FromResult("br-body");
        }, TestContext.Current.CancellationToken);

        // Same encoding again → hit
        DefaultHttpContext gzip2 = CreateHttp("/api/enc");
        gzip2.Request.Headers.AcceptEncoding = "gzip";
        domains.EnsureDomainOptions(gzip2, domain);

        string g2 = await cache.GetOrSetAsync(gzip2, _ =>
        {
            factoryCalls++;
            return Task.FromResult("should-not-run");
        }, TestContext.Current.CancellationToken);

        g1.Should().Be("gzip-body");
        b1.Should().Be("br-body");
        g2.Should().Be("gzip-body");
        factoryCalls.Should().Be(2, "gzip and br must be independent Fusion entries; third call is a hit");
        Disposition(gzip2)!.Data.Should().Be(DataCacheResult.Hit);
    }

    // =========================================================================
    // B20 — FusionCacheEnabled false over HTTP
    // =========================================================================

    [Fact]
    public async Task FusionDisabled_HttpEndpoint_AlwaysRunsFactory()
    {
        string domain = "fc-off-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = DomainBase(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCacheEnabled"] = "false";
            d[$"Cache:Domains:{domain}:FusionCacheEnabled"] = "false";
        });

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(config, a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainFusionCache cache, FactoryCounter factory) =>
            {
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    factory.Increment();
                    return Task.FromResult("n" + factory.Count);
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            b1.Should().Be("n1");
            x1.Should().Contain("data=off");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            b2.Should().Be("n2");
            x2.Should().Contain("data=off");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
