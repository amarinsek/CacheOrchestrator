using CacheOrchestrator.Configuration;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Behavior;

public class CacheOrchestratorHeaderTests
{
    private sealed class HitCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private static Dictionary<string, string?> DomainConfig(
        string domain,
        bool outputEnabled = true,
        int clientTtl = 60,
        int outputTtl = 60,
        int fusionTtl = 60)
    {
        Dictionary<string, string?> d = new()
        {
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientCache:TtlSeconds"] = clientTtl.ToString(),
            [$"Cache:Domains:{domain}:ClientCache:TtlMinSeconds"] = clientTtl.ToString(),
            [$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = outputTtl.ToString(),
            [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = fusionTtl.ToString()
        };
        if (!outputEnabled)
            d[$"Cache:Domains:{domain}:OutputCache:Enabled"] = "false";
        return d;
    }

    private static async Task<(HttpClient client, WebApplication app)> StartAsync(
        Dictionary<string, string?> extraConfig,
        Action<WebApplication>? map = null)
    {
        Dictionary<string, string?> configValues = new()
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCache:Provider"] = "InMemory"
        };
        foreach (KeyValuePair<string, string?> kv in extraConfig)
            configValues[kv.Key] = kv.Value;

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestratorAspNetCore(config);
        builder.Services.AddCacheOrchestratorFusionCache(config);
        builder.Services.AddSingleton<HitCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        if (map is not null)
        {
            map(app);
        }
        else
        {
            string domain = extraConfig.Keys
                .Select(k => k.StartsWith("Cache:Domains:") ? k.Split(':')[2] : null)
                .FirstOrDefault(d => d is not null) ?? "demo";

            app.MapGet("/x", async (HttpContext http, HitCounter hits) =>
            {
                hits.Increment();
                await Task.Delay(20);
                return Results.Text("body-" + DateTimeOffset.UtcNow.Ticks);
            })
            .CacheOutputWithDomain(domain);
        }

        await app.StartAsync();
        return (app.GetTestClient(), app);
    }

    private static async Task<(HttpResponseMessage res, string xCache, string body)> GetAsync(
        HttpClient client, string url, Dictionary<string, string>? requestHeaders = null)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, url);
        if (requestHeaders is not null)
        {
            foreach (KeyValuePair<string, string> h in requestHeaders)
                req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        HttpResponseMessage res = await client.SendAsync(req, TestContext.Current.CancellationToken);
        string body = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string xCache = res.Headers.TryGetValues("X-CacheOrchestrator", out IEnumerable<string>? values)
            ? string.Join(",", values)
            : "";
        return (res, xCache, body);
    }

    private static void AssertCacheOrchestratorHeader(
        string xCache,
        string domain,
        string client,
        string output,
        string? data = null)
    {
        xCache.Should().Contain($"domain={domain}");
        xCache.Should().Contain($"client={client}");
        xCache.Should().Contain("phase="); // Client Cache Schedule phase is always present
        xCache.Should().Contain($"oc={output}");
        if (data is null)
        {
            if (output == "hit")
            {
                xCache.Should().NotContain("dc=");
                xCache.Should().NotContain("fa=");
            }
            else
            {
                xCache.Should().Contain("dc=n/a");
                xCache.Should().Contain("fa=run");
            }
        }
        else
        {
            xCache.Should().Contain($"dc={data}");
            if (data == "hit")
                xCache.Should().NotContain("fa=");
            else
                xCache.Should().Contain("fa=run");
        }
    }

    // =========================================================================
    // 1) First request ? oc=miss; dc=miss; fa=run
    // =========================================================================

    [Fact]
    public async Task FirstRequest_Is_DataMiss()
    {
        string domain = "x-miss-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain), app =>
        {
            app.MapGet("/x", async (HttpContext http, IDomainDataCache cache, IRequestDomainCacheOptions domains, HitCounter hits) =>
            {
                domains.EnsureDomainOptions(http, domain);
                string value = await cache.GetOrSetAsync(http, async _ =>
                {
                    hits.Increment();
                    await Task.Delay(15);
                    return "v1";
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage? res, string? xCache, string? body) = await GetAsync(client, "/x");
            res.IsSuccessStatusCode.Should().BeTrue();
            body.Should().Be("v1");
            AssertCacheOrchestratorHeader(xCache, domain, "public", "miss", "miss");
            xCache.Should().Contain("ms=");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 2) Second request ? oc=hit
    // =========================================================================

    [Fact]
    public async Task SecondRequest_Is_OutputHit()
    {
        string domain = "x-ochit-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain), app =>
        {
            app.MapGet("/x", async (HttpContext http, IDomainDataCache cache, IRequestDomainCacheOptions domains, HitCounter hits) =>
            {
                domains.EnsureDomainOptions(http, domain);
                string value = await cache.GetOrSetAsync(http, async _ =>
                {
                    hits.Increment();
                    await Task.Delay(15);
                    return "v1";
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage? r1, string? x1, string _) = await GetAsync(client, "/x");
            r1.IsSuccessStatusCode.Should().BeTrue();
            AssertCacheOrchestratorHeader(x1, domain, "public", "miss", "miss");

            (HttpResponseMessage? r2, string? x2, string? body2) = await GetAsync(client, "/x");
            r2.IsSuccessStatusCode.Should().BeTrue();
            body2.Should().Be("v1");
            AssertCacheOrchestratorHeader(x2, domain, "public", "hit", data: null);
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 3) After Invalidate ? miss again
    // =========================================================================

    [Fact]
    public async Task AfterInvalidate_IsMissAgain()
    {
        string domain = "x-inv-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain), app =>
        {
            app.MapGet("/x", async (HttpContext http, IDomainDataCache cache, IRequestDomainCacheOptions domains, HitCounter hits) =>
            {
                domains.EnsureDomainOptions(http, domain);
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    hits.Increment();
                    return Task.FromResult("v1");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            await GetAsync(client, "/x");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            await app.Services.GetRequiredService<ICacheOrchestratorInvalidator>()
                .InvalidateDomainAsync(domain, TestContext.Current.CancellationToken);

            (HttpResponseMessage? res, string? xCache, string _) = await GetAsync(client, "/x");
            res.IsSuccessStatusCode.Should().BeTrue();
            AssertCacheOrchestratorHeader(xCache, domain, "public", "miss", "miss");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 4) Output disabled, no OC policy ? endpoint+Fusion only (no X-CacheOrchestrator required)
    // =========================================================================

    [Fact]
    public async Task OutputDisabled_SecondRequest_FusionHits_FactoryOnce()
    {
        string domain = "x-fchit-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain, outputEnabled: false), app =>
        {
            app.MapGet("/x", async (HttpContext http, IDomainDataCache cache, IRequestDomainCacheOptions domains, HitCounter hits) =>
            {
                domains.EnsureDomainOptions(http, domain);
                string value = await cache.GetOrSetAsync(http, async _ =>
                {
                    hits.Increment();
                    await Task.Delay(15);
                    return "fusion-value";
                }, http.RequestAborted);
                return Results.Text(value);
            });
        });

        try
        {
            (HttpResponseMessage? r1, string _, string? b1) = await GetAsync(client, "/x");
            r1.IsSuccessStatusCode.Should().BeTrue();
            b1.Should().Be("fusion-value");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            (HttpResponseMessage? r2, string _, string? b2) = await GetAsync(client, "/x");
            r2.IsSuccessStatusCode.Should().BeTrue();
            b2.Should().Be("fusion-value");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 5) OC disabled but policy attached ? oc=off; second call dc=hit
    // =========================================================================

    [Fact]
    public async Task OutputCacheEnabledFalse_WithPolicy_SecondCall_DataHit()
    {
        string domain = "x-ocoff-" + Guid.NewGuid().ToString("N");
        int[] fusionCalls = new int[1];

        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain, outputEnabled: false), app =>
        {
            app.MapGet("/x", async (HttpContext http, IDomainDataCache cache, IRequestDomainCacheOptions domains, HitCounter hits) =>
            {
                hits.Increment();
                domains.EnsureDomainOptions(http, domain);
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    Interlocked.Increment(ref fusionCalls[0]);
                    return Task.FromResult("v");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage _, string? x1, string _) = await GetAsync(client, "/x");
            AssertCacheOrchestratorHeader(x1, domain, "public", "off", "miss");

            (HttpResponseMessage _, string? x2, string _) = await GetAsync(client, "/x");
            AssertCacheOrchestratorHeader(x2, domain, "public", "off", "hit");

            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
            Volatile.Read(ref fusionCalls[0]).Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 6) Set-Cookie ? client=blocked
    // =========================================================================

    [Fact]
    public async Task SetCookie_Yields_ClientBlocked()
    {
        string domain = "x-cookie-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain), app =>
        {
            app.MapGet("/x", (HttpContext http) =>
            {
                http.Response.Headers.Append("Set-Cookie", "session=abc; Path=/");
                return Results.Text("cookie-body");
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage? res, string? xCache, string? body) = await GetAsync(client, "/x");
            res.IsSuccessStatusCode.Should().BeTrue();
            body.Should().Be("cookie-body");
            AssertCacheOrchestratorHeader(xCache, domain, "blocked", "miss", data: null);
            string cc = res.Headers.TryGetValues("Cache-Control", out IEnumerable<string>? v) ? string.Join(",", v) : "";
            cc.Should().Contain("no-store");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 7) Client TTL reflected in Cache-Control
    // =========================================================================

    [Fact]
    public async Task ClientTtl_IsWritten_OnCacheControl()
    {
        string domain = "x-cc-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain, clientTtl: 77), app =>
        {
            app.MapGet("/x", () => Results.Text("ok")).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage? res, string? xCache, string _) = await GetAsync(client, "/x");
            res.IsSuccessStatusCode.Should().BeTrue();
            AssertCacheOrchestratorHeader(xCache, domain, "public", "miss", data: null);
            string cc = res.Headers.TryGetValues("Cache-Control", out IEnumerable<string>? v) ? string.Join(",", v) : "";
            cc.Should().Contain("max-age=77");
            cc.Should().Contain("public");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 8) Tracking query does not fragment OC
    // =========================================================================

    [Fact]
    public async Task TrackingQuery_DoesNotFragment_OutputCache()
    {
        string domain = "x-utm-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain), app =>
        {
            app.MapGet("/x", async (HttpContext http, IDomainDataCache cache, IRequestDomainCacheOptions domains, HitCounter hits) =>
            {
                domains.EnsureDomainOptions(http, domain);
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    hits.Increment();
                    return Task.FromResult("same");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage? r1, string? x1, string _) = await GetAsync(client, "/x?utm_source=google");
            r1.IsSuccessStatusCode.Should().BeTrue();
            AssertCacheOrchestratorHeader(x1, domain, "public", "miss", "miss");

            (HttpResponseMessage? r2, string? x2, string? body2) = await GetAsync(client, "/x");
            r2.IsSuccessStatusCode.Should().BeTrue();
            body2.Should().Be("same");
            AssertCacheOrchestratorHeader(x2, domain, "public", "hit", data: null);
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 9) Different real query ? separate entries
    // =========================================================================

    [Fact]
    public async Task DifferentQuery_CreatesSeparateEntries()
    {
        string domain = "x-q-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain), app =>
        {
            app.MapGet("/x", (HttpContext http, HitCounter hits) =>
            {
                hits.Increment();
                string q = http.Request.Query["q"].ToString();
                return Results.Text("q=" + q);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage? r1, string _, string? b1) = await GetAsync(client, "/x?q=a");
            (HttpResponseMessage? r2, string _, string? b2) = await GetAsync(client, "/x?q=b");
            r1.IsSuccessStatusCode.Should().BeTrue();
            r2.IsSuccessStatusCode.Should().BeTrue();
            b1.Should().Be("q=a");
            b2.Should().Be("q=b");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 10) POST is not output-cached
    // =========================================================================

    [Fact]
    public async Task Post_IsNotOutputCached()
    {
        string domain = "x-post-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain), app =>
        {
            app.MapPost("/x", (HitCounter hits) =>
            {
                hits.Increment();
                return Results.Text("posted");
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            HttpResponseMessage r1 = await client.PostAsync("/x", null, TestContext.Current.CancellationToken);
            HttpResponseMessage r2 = await client.PostAsync("/x", null, TestContext.Current.CancellationToken);
            r1.IsSuccessStatusCode.Should().BeTrue();
            r2.IsSuccessStatusCode.Should().BeTrue();
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // 11) EmitDiagnosticsHeaders controls client-visible X-CacheOrchestrator
    // =========================================================================

    [Fact]
    public async Task EmitDiagnosticsHeaders_False_OmitsCacheOrchestratorHeader_StillSetsCacheControl()
    {
        string domain = "x-nodiag-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = DomainConfig(domain, clientTtl: 55);
        config["Cache:EmitDiagnosticsHeaders"] = "false";

        (HttpClient? client, WebApplication? app) = await StartAsync(config, app =>
        {
            app.MapGet("/x", () => Results.Text("ok")).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage? res, string? xCache, string? body) = await GetAsync(client, "/x");
            res.IsSuccessStatusCode.Should().BeTrue();
            body.Should().Be("ok");
            xCache.Should().BeEmpty();
            res.Headers.Contains("X-CacheOrchestrator").Should().BeFalse();

            string cc = res.Headers.TryGetValues("Cache-Control", out IEnumerable<string>? v)
                ? string.Join(",", v)
                : "";
            cc.Should().Contain("max-age=55");
            cc.Should().Contain("public");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task EmitDiagnosticsHeaders_Default_EmitsCacheOrchestratorHeader()
    {
        string domain = "x-diag-on-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(DomainConfig(domain), app =>
        {
            app.MapGet("/x", () => Results.Text("ok")).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage? res, string? xCache, string _) = await GetAsync(client, "/x");
            res.IsSuccessStatusCode.Should().BeTrue();
            res.Headers.Contains("X-CacheOrchestrator").Should().BeTrue();
            AssertCacheOrchestratorHeader(xCache, domain, "public", "miss", data: null);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
