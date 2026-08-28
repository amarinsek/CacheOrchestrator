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
using System.Security.Claims;

namespace CacheOrchestrator.IntegrationTests.OutputCaching;

/// <summary>
/// HTTP lifecycle coverage for Output Cache (TestServer + HttpClient): TTL expiry vs Fusion,
/// no-store, auth bypass / per-user vary, ETag modes, status codes, methods, entity tags,
/// domain templates, dynamic domains, and host vary.
/// </summary>
public class OutputCacheHttpLifecycleTests
{
    private sealed class HitCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class FactoryCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private static async Task<(HttpClient Client, WebApplication App)> StartAsync(
        Dictionary<string, string?> configValues,
        Action<WebApplication> map,
        bool attachTestUserMiddleware = false)
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
        builder.Services.AddCacheOrchestratorAspNetCore(config);
        builder.Services.AddCacheOrchestratorFusionCache(config);
        builder.Services.AddSingleton<HitCounter>();
        builder.Services.AddSingleton<FactoryCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();

        if (attachTestUserMiddleware)
        {
            // Sets an authenticated principal from X-Test-User for BypassWhenAuthenticated / vary-by-user scenarios.
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Headers.TryGetValue("X-Test-User", out Microsoft.Extensions.Primitives.StringValues user)
                    && !string.IsNullOrWhiteSpace(user))
                {
                    ClaimsIdentity identity = new(
                        [new Claim(ClaimTypes.Name, user.ToString())],
                        authenticationType: "test");
                    ctx.User = new ClaimsPrincipal(identity);
                }

                await next();
            });
        }

        app.UseCacheOrchestrator();
        map(app);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return (app.GetTestClient(), app);
    }

    private static Dictionary<string, string?> BaseConfig(string domain, Action<Dictionary<string, string?>>? extra = null)
    {
        Dictionary<string, string?> d = new()
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientCache:TtlSeconds"] = "60",
            [$"Cache:Domains:{domain}:ClientCache:TtlMinSeconds"] = "60",
            [$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = "120",
            [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "300",
            [$"Cache:Domains:{domain}:FusionCache:JitterSeconds"] = "0",
            [$"Cache:Domains:{domain}:FusionCache:EagerRefreshRatio"] = "0",
        };
        extra?.Invoke(d);
        return d;
    }

    private static Dictionary<string, string?> DefaultsOnlyConfig(Action<Dictionary<string, string?>>? extra = null)
    {
        Dictionary<string, string?> d = new()
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            ["Cache:DomainDefaults:Version"] = "v1",
            ["Cache:DomainDefaults:ClientCache:Cacheability"] = "Public",
            ["Cache:DomainDefaults:ClientCache:TtlSeconds"] = "60",
            ["Cache:DomainDefaults:ClientCache:TtlMinSeconds"] = "60",
            ["Cache:DomainDefaults:OutputCache:TtlSeconds"] = "120",
            ["Cache:DomainDefaults:DataCache:TtlSeconds"] = "300",
        };
        extra?.Invoke(d);
        return d;
    }

    private static async Task<(HttpResponseMessage Res, string XCache, string Body)> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        Dictionary<string, string>? headers = null)
    {
        using HttpRequestMessage req = new(method, url);
        if (headers is not null)
        {
            foreach (KeyValuePair<string, string> h in headers)
                req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        HttpResponseMessage res = await client.SendAsync(req, TestContext.Current.CancellationToken);
        string body = method == HttpMethod.Head
            ? string.Empty
            : await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string xCache = res.Headers.TryGetValues("X-Cache", out IEnumerable<string>? values)
            ? string.Join(",", values)
            : string.Empty;
        return (res, xCache, body);
    }

    private static Task<(HttpResponseMessage Res, string XCache, string Body)> GetAsync(
        HttpClient client,
        string url,
        Dictionary<string, string>? headers = null)
        => SendAsync(client, HttpMethod.Get, url, headers);

    private static string? ETag(HttpResponseMessage res) =>
        res.Headers.ETag?.Tag
        ?? (res.Headers.TryGetValues("ETag", out IEnumerable<string>? v) ? v.FirstOrDefault() : null);

    private static string CacheControl(HttpResponseMessage res) =>
        res.Headers.TryGetValues("Cache-Control", out IEnumerable<string>? v)
            ? string.Join(",", v)
            : string.Empty;

    // =========================================================================
    // A1 — OC TTL expires → output miss; Fusion still hits (factory not re-run)
    // =========================================================================

    [Fact]
    public async Task OutputTtl_Expires_ThenMiss_WhileFusionStillHits()
    {
        string domain = "oc-ttl-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = BaseConfig(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = "1";
            d[$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "300";
            d[$"Cache:Domains:{domain}:FusionCache:HardTtlSeconds"] = "3600";
        });

        (HttpClient? client, WebApplication? app) = await StartAsync(config, a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainDataCache cache, HitCounter hits, FactoryCounter factory) =>
            {
                hits.Increment();
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    factory.Increment();
                    return Task.FromResult("payload");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            r1.IsSuccessStatusCode.Should().BeTrue();
            b1.Should().Be("payload");
            x1.Should().Contain("oc=miss");
            x1.Should().Contain("dc=miss");
            x1.Should().Contain("fa=run");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            // Still within OC TTL → full response from Output Cache (handler not run).
            (HttpResponseMessage r2, string x2, string _) = await GetAsync(client, "/x");
            x2.Should().Contain("oc=hit");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);

            // OC expired: endpoint runs again, Fusion serves data hit without re-running factory.
            (HttpResponseMessage r3, string x3, string b3) = await GetAsync(client, "/x");
            r3.IsSuccessStatusCode.Should().BeTrue();
            b3.Should().Be("payload");
            x3.Should().Contain("oc=miss");
            x3.Should().Contain("dc=hit");
            x3.Should().NotContain("fa=");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1,
                "Fusion soft TTL is long; only Output Cache expired");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A2 — Request Cache-Control: no-store
    // =========================================================================

    [Fact]
    public async Task RequestNoStore_BypassesOutputCache_AndSetsClientNoStore()
    {
        string domain = "oc-ns-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(BaseConfig(domain), a =>
        {
            a.MapGet("/x", (HitCounter hits) =>
            {
                hits.Increment();
                return Results.Text("body");
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            Dictionary<string, string> noStore = new() { ["Cache-Control"] = "no-store" };

            (HttpResponseMessage r1, string x1, string _) = await GetAsync(client, "/x", noStore);
            r1.IsSuccessStatusCode.Should().BeTrue();
            x1.Should().Contain("oc=bypass");
            x1.Should().Contain("client=no-store");
            CacheControl(r1).Should().Contain("no-store");

            (HttpResponseMessage r2, string x2, string _) = await GetAsync(client, "/x", noStore);
            x2.Should().Contain("oc=bypass");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2,
                "no-store must not store or serve from Output Cache");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A3 — Default auth / Authorization bypass
    // =========================================================================

    [Fact]
    public async Task AuthorizationHeader_Default_BypassesOutputCache_ClientBlocked()
    {
        string domain = "oc-authz-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(BaseConfig(domain), a =>
        {
            a.MapGet("/x", (HitCounter hits) =>
            {
                hits.Increment();
                return Results.Text("secret");
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            Dictionary<string, string> auth = new() { ["Authorization"] = "Bearer token-a" };

            (HttpResponseMessage r1, string x1, string _) = await GetAsync(client, "/x", auth);
            r1.IsSuccessStatusCode.Should().BeTrue();
            x1.Should().Contain("oc=bypass");
            x1.Should().Contain("client=blocked");
            CacheControl(r1).Should().Contain("no-store");

            (HttpResponseMessage r2, string x2, string _) = await GetAsync(client, "/x", auth);
            x2.Should().Contain("oc=bypass");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthenticatedUser_Default_BypassesOutputCache()
    {
        string domain = "oc-user-" + Guid.NewGuid().ToString("N");
        (HttpClient? client, WebApplication? app) = await StartAsync(
            BaseConfig(domain),
            a =>
            {
                a.MapGet("/x", (HitCounter hits) =>
                {
                    hits.Increment();
                    return Results.Text("me");
                }).CacheOutputWithDomain(domain);
            },
            attachTestUserMiddleware: true);

        try
        {
            Dictionary<string, string> headers = new() { ["X-Test-User"] = "alice" };

            (HttpResponseMessage r1, string x1, string _) = await GetAsync(client, "/x", headers);
            x1.Should().Contain("oc=bypass");
            x1.Should().Contain("client=blocked");

            await GetAsync(client, "/x", headers);
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A4 — BypassWhenAuthenticated false + VaryOutputCacheByUser true
    // =========================================================================

    [Fact]
    public async Task AuthAllowed_VaryByUser_PartitionsOutputCachePerUser()
    {
        // Prefer Authorization-header identity (hashed vary key). Cookie-style principals are
        // covered by AuthenticatedUser_* defaults; here we need stable OC storage + partition.
        string domain = "oc-varyu-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = BaseConfig(domain, d =>
        {
            d[$"Cache:Domains:{domain}:AuthBypassMode"] = "Never";
            d[$"Cache:Domains:{domain}:VaryOutputCacheByUser"] = "true";
            d[$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Private";
        });

        (HttpClient? client, WebApplication? app) = await StartAsync(config, a =>
        {
            a.MapGet("/x", (HttpContext http, HitCounter hits) =>
            {
                hits.Increment();
                string auth = http.Request.Headers.Authorization.ToString();
                string who = auth.Contains("alice", StringComparison.Ordinal) ? "alice" : "bob";
                return Results.Text("hello-" + who);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            Dictionary<string, string> alice = new() { ["Authorization"] = "Bearer alice-token" };
            Dictionary<string, string> bob = new() { ["Authorization"] = "Bearer bob-token" };

            (HttpResponseMessage a1, string xa1, string ba1) = await GetAsync(client, "/x", alice);
            ba1.Should().Be("hello-alice");
            xa1.Should().Contain("oc=miss");

            (HttpResponseMessage a2, string xa2, string ba2) = await GetAsync(client, "/x", alice);
            ba2.Should().Be("hello-alice");
            xa2.Should().Contain("oc=hit");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            (HttpResponseMessage b1, string xb1, string bb1) = await GetAsync(client, "/x", bob);
            bb1.Should().Be("hello-bob");
            xb1.Should().Contain("oc=miss", "Bob must not receive Alice's OC entry");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);

            (HttpResponseMessage b2, string xb2, string _) = await GetAsync(client, "/x", bob);
            xb2.Should().Contain("oc=hit");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A5 — BypassWhenAuthenticated false + VaryOutputCacheByUser false (shared OC)
    // =========================================================================

    [Fact]
    public async Task AuthAllowed_VaryByUserFalse_SharesOutputCacheAcrossApiKeys()
    {
        string domain = "oc-shared-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = BaseConfig(domain, d =>
        {
            d[$"Cache:Domains:{domain}:AuthBypassMode"] = "Never";
            d[$"Cache:Domains:{domain}:VaryOutputCacheByUser"] = "false";
        });

        (HttpClient? client, WebApplication? app) = await StartAsync(config, a =>
        {
            a.MapGet("/x", (HitCounter hits) =>
            {
                hits.Increment();
                return Results.Text("public-map-tile");
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            Dictionary<string, string> keyA = new() { ["Authorization"] = "Bearer key-aaa" };
            Dictionary<string, string> keyB = new() { ["Authorization"] = "Bearer key-bbb" };

            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x", keyA);
            b1.Should().Be("public-map-tile");
            x1.Should().Contain("oc=miss");

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x", keyB);
            b2.Should().Be("public-map-tile");
            x2.Should().Contain("oc=hit", "same body for all API keys when VaryOutputCacheByUser is false");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A6 — ETag modes
    // =========================================================================

    [Fact]
    public async Task ETagMode_Version_SameETagAcrossPaths_ResourceDiffers_NoneOmits()
    {
        string domainVer = "oc-etag-v-" + Guid.NewGuid().ToString("N");
        string domainRes = "oc-etag-r-" + Guid.NewGuid().ToString("N");
        string domainNone = "oc-etag-n-" + Guid.NewGuid().ToString("N");

        Dictionary<string, string?> config = new()
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
        };

        void Domain(string name, string etagMode)
        {
            config[$"Cache:Domains:{name}:Version"] = "gen-1";
            config[$"Cache:Domains:{name}:OutputCache:ETagMode"] = etagMode;
            config[$"Cache:Domains:{name}:ClientCache:Cacheability"] = "Public";
            config[$"Cache:Domains:{name}:ClientCache:TtlSeconds"] = "60";
            config[$"Cache:Domains:{name}:ClientCache:TtlMinSeconds"] = "60";
            config[$"Cache:Domains:{name}:OutputCache:TtlSeconds"] = "120";
        }

        Domain(domainVer, "Version");
        Domain(domainRes, "Resource");
        Domain(domainNone, "None");

        (HttpClient? client, WebApplication? app) = await StartAsync(config, a =>
        {
            a.MapGet("/ver/{id}", (string id) => Results.Text("v-" + id))
                .CacheOutputWithDomain(domainVer);
            a.MapGet("/res/{id}", (string id) => Results.Text("r-" + id))
                .CacheOutputWithDomain(domainRes, resourceRouteKey: "id", entityKind: "items");
            a.MapGet("/none", () => Results.Text("n"))
                .CacheOutputWithDomain(domainNone);
        });

        try
        {
            (HttpResponseMessage v1, _, _) = await GetAsync(client, "/ver/1");
            (HttpResponseMessage v2, _, _) = await GetAsync(client, "/ver/2");
            string? etagV1 = ETag(v1);
            string? etagV2 = ETag(v2);
            etagV1.Should().NotBeNullOrWhiteSpace();
            etagV2.Should().Be(etagV1, "ETagMode.Version is a domain generation stamp");

            (HttpResponseMessage r1, _, _) = await GetAsync(client, "/res/1");
            (HttpResponseMessage r2, _, _) = await GetAsync(client, "/res/2");
            string? etagR1 = ETag(r1);
            string? etagR2 = ETag(r2);
            etagR1.Should().NotBeNullOrWhiteSpace();
            etagR2.Should().NotBe(etagR1, "ETagMode.Resource must differ per resource id");

            (HttpResponseMessage n1, _, _) = await GetAsync(client, "/none");
            ETag(n1).Should().BeNullOrEmpty("ETagMode.None must omit ETag");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A7 — Non-cacheable status (500) is not stored
    // =========================================================================

    [Fact]
    public async Task Status500_IsNotStoredInOutputCache()
    {
        string domain = "oc-500-" + Guid.NewGuid().ToString("N");
        int[] phase = [0];

        (HttpClient? client, WebApplication? app) = await StartAsync(BaseConfig(domain), a =>
        {
            a.MapGet("/x", (HitCounter hits) =>
            {
                hits.Increment();
                int n = Interlocked.Increment(ref phase[0]);
                if (n == 1)
                    return Results.Text("fail", statusCode: StatusCodes.Status500InternalServerError);
                return Results.Text("ok");
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            r1.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
            b1.Should().Be("fail");
            x1.Should().Contain("oc=miss");

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            r2.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
            b2.Should().Be("ok");
            x2.Should().Contain("oc=miss", "500 response must not be served from Output Cache");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A8 — HEAD cacheable; PUT not
    // =========================================================================

    [Fact]
    public async Task Head_IsOutputCached_PutIsNot()
    {
        string domain = "oc-methods-" + Guid.NewGuid().ToString("N");

        (HttpClient? client, WebApplication? app) = await StartAsync(BaseConfig(domain), a =>
        {
            a.MapMethods("/x", [HttpMethods.Get, HttpMethods.Head], (HitCounter hits) =>
            {
                hits.Increment();
                return Results.Text("head-body");
            }).CacheOutputWithDomain(domain);

            a.MapMethods("/y", [HttpMethods.Put], (HitCounter hits) =>
            {
                hits.Increment();
                return Results.Text("put-body");
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage h1, string xh1, string _) = await SendAsync(client, HttpMethod.Head, "/x");
            h1.IsSuccessStatusCode.Should().BeTrue();
            // First HEAD may miss; second HEAD should hit without re-running handler.
            (HttpResponseMessage h2, string xh2, string _) = await SendAsync(client, HttpMethod.Head, "/x");
            h2.IsSuccessStatusCode.Should().BeTrue();

            // ASP.NET Output Cache may share GET/HEAD for same resource; assert handler not run twice for pure HEAD pair.
            // If first was miss and second hit: count stays 1.
            int afterHead = app.Services.GetRequiredService<HitCounter>().Count;
            afterHead.Should().BeLessThanOrEqualTo(1);
            if (xh2.Contains("oc=hit", StringComparison.Ordinal))
                afterHead.Should().Be(1);

            // Also warm GET then HEAD when GET is used by the stack.
            await GetAsync(client, "/x");
            int afterGet = app.Services.GetRequiredService<HitCounter>().Count;
            (HttpResponseMessage h3, string xh3, string _) = await SendAsync(client, HttpMethod.Head, "/x");
            h3.IsSuccessStatusCode.Should().BeTrue();
            // After a successful GET store, HEAD should typically hit; handler count must not grow on hit.
            if (xh3.Contains("oc=hit", StringComparison.Ordinal))
                app.Services.GetRequiredService<HitCounter>().Count.Should().Be(afterGet);

            int beforePut = app.Services.GetRequiredService<HitCounter>().Count;
            (HttpResponseMessage p1, string xp1, string _) = await SendAsync(client, HttpMethod.Put, "/y");
            p1.IsSuccessStatusCode.Should().BeTrue();
            (HttpResponseMessage p2, string xp2, string _) = await SendAsync(client, HttpMethod.Put, "/y");
            p2.IsSuccessStatusCode.Should().BeTrue();
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(beforePut + 2,
                "PUT must never be served from Output Cache");
            // X-Cache may be empty when policy skips non-GET/HEAD before registering headers.
            if (!string.IsNullOrEmpty(xp1))
                xp1.Should().NotContain("oc=hit");
            if (!string.IsNullOrEmpty(xp2))
                xp2.Should().NotContain("oc=hit");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A9 — resourceRouteKey + InvalidateEntityAsync purges OC
    // =========================================================================

    [Fact]
    public async Task ResourceRouteKey_InvalidateEntity_PurgesOnlyThatOutputCacheEntry()
    {
        string domain = "oc-ent-" + Guid.NewGuid().ToString("N");

        (HttpClient? client, WebApplication? app) = await StartAsync(BaseConfig(domain), a =>
        {
            a.MapGet("/p/{id}", (string id, HitCounter hits) =>
            {
                hits.Increment();
                return Results.Text("product-" + id);
            }).CacheOutputWithDomain(domain, resourceRouteKey: "id", entityKind: "items");
        });

        try
        {
            await GetAsync(client, "/p/1");
            await GetAsync(client, "/p/2");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);

            (HttpResponseMessage hit1, string xHit1, string _) = await GetAsync(client, "/p/1");
            xHit1.Should().Contain("oc=hit");
            (HttpResponseMessage hit2, string xHit2, string _) = await GetAsync(client, "/p/2");
            xHit2.Should().Contain("oc=hit");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);

            CacheInvalidationResult inv = await app.Services
                .GetRequiredService<ICacheOrchestratorInvalidator>()
                .InvalidateEntityAsync(domain, "items", "1", TestContext.Current.CancellationToken);
            inv.Succeeded.Should().BeTrue(string.Join("; ", inv.Errors));

            (HttpResponseMessage after1, string xAfter1, string b1) = await GetAsync(client, "/p/1");
            b1.Should().Be("product-1");
            xAfter1.Should().Contain("oc=miss");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(3);

            (HttpResponseMessage after2, string xAfter2, string b2) = await GetAsync(client, "/p/2");
            b2.Should().Be("product-2");
            xAfter2.Should().Contain("oc=hit", "entity invalidate must not purge sibling ids");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(3);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A10 — CacheOutputWithDomainTemplate
    // =========================================================================

    [Fact]
    public async Task DomainTemplate_HostToken_PartitionsByHostAndDomain()
    {
        // tenant-{host} → normalized domain e.g. tenant-shop1-example-com
        Dictionary<string, string?> config = DefaultsOnlyConfig(d =>
        {
            d["Cache:Domains:tenant-shop1-example-com:Version"] = "v1";
            d["Cache:Domains:tenant-shop2-example-com:Version"] = "v1";
        });

        (HttpClient? client, WebApplication? app) = await StartAsync(config, a =>
        {
            a.MapGet("/tiles", (HttpContext http, HitCounter hits) =>
            {
                hits.Increment();
                return Results.Text("tile-for-" + http.Request.Host.Host);
            }).CacheOutputWithDomainTemplate("tenant-{host}");
        });

        try
        {
            Dictionary<string, string> host1 = new() { ["Host"] = "shop1.example.com" };
            Dictionary<string, string> host2 = new() { ["Host"] = "shop2.example.com" };

            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/tiles", host1);
            b1.Should().Be("tile-for-shop1.example.com");
            x1.Should().Contain("oc=miss");
            x1.Should().Contain("domain=tenant-shop1-example-com");

            (HttpResponseMessage r1b, string x1b, string _) = await GetAsync(client, "/tiles", host1);
            x1b.Should().Contain("oc=hit");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/tiles", host2);
            b2.Should().Be("tile-for-shop2.example.com");
            x2.Should().Contain("oc=miss");
            x2.Should().Contain("domain=tenant-shop2-example-com");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A11 — CacheOutputWithDomain(func)
    // =========================================================================

    [Fact]
    public async Task DynamicDomainFunc_FromQuery_UsesResolvedDomain()
    {
        Dictionary<string, string?> config = DefaultsOnlyConfig(d =>
        {
            d["Cache:Domains:dyn-acme:Version"] = "v1";
            d["Cache:Domains:dyn-globex:Version"] = "v1";
        });

        (HttpClient? client, WebApplication? app) = await StartAsync(config, a =>
        {
            a.MapGet("/data", (HttpContext http, HitCounter hits) =>
            {
                hits.Increment();
                string tenant = http.Request.Query["tenant"].ToString();
                return Results.Text("data-" + tenant);
            }).CacheOutputWithDomain(http => "dyn-" + http.Request.Query["tenant"].ToString());
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/data?tenant=acme");
            b1.Should().Be("data-acme");
            x1.Should().Contain("domain=dyn-acme");
            x1.Should().Contain("oc=miss");

            (HttpResponseMessage r2, string x2, string _) = await GetAsync(client, "/data?tenant=acme");
            x2.Should().Contain("oc=hit");

            (HttpResponseMessage r3, string x3, string b3) = await GetAsync(client, "/data?tenant=globex");
            b3.Should().Be("data-globex");
            x3.Should().Contain("domain=dyn-globex");
            x3.Should().Contain("oc=miss");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // =========================================================================
    // A12 — Host vary
    // =========================================================================

    [Fact]
    public async Task VaryByHost_DifferentHosts_AreIndependentOutputCacheEntries()
    {
        string domain = "oc-host-" + Guid.NewGuid().ToString("N");

        (HttpClient? client, WebApplication? app) = await StartAsync(BaseConfig(domain), a =>
        {
            a.MapGet("/x", (HttpContext http, HitCounter hits) =>
            {
                hits.Increment();
                return Results.Text("host-" + http.Request.Host.Value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            Dictionary<string, string> h1 = new() { ["Host"] = "a.example.com" };
            Dictionary<string, string> h2 = new() { ["Host"] = "b.example.com" };

            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x", h1);
            b1.Should().Be("host-a.example.com");
            x1.Should().Contain("oc=miss");

            (HttpResponseMessage r1b, string x1b, string _) = await GetAsync(client, "/x", h1);
            x1b.Should().Contain("oc=hit");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(1);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x", h2);
            b2.Should().Be("host-b.example.com");
            x2.Should().Contain("oc=miss");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);

            (HttpResponseMessage r2b, string x2b, string _) = await GetAsync(client, "/x", h2);
            x2b.Should().Contain("oc=hit");
            app.Services.GetRequiredService<HitCounter>().Count.Should().Be(2);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
