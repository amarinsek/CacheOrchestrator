using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Fusion;

/// <summary>
/// HTTP coverage for Fusion contracts that are easy to get wrong on a real request:
/// entity-then-URL key restore, Accept restore, explicit domain vs OC domain,
/// DataCacheRespectAuthBypass, and no-store through both layers.
/// </summary>
public class FusionHttpContractTests
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
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientCache:Ttl"] = "00:01:00",
            [$"Cache:Domains:{domain}:ClientCache:TtlMin"] = "00:01:00",
            [$"Cache:Domains:{domain}:OutputCache:Ttl"] = "00:02:00",
            [$"Cache:Domains:{domain}:DataCache:Ttl"] = "00:05:00",
            [$"Cache:Domains:{domain}:DataCache:Jitter"] = "00:00:00",
            [$"Cache:Domains:{domain}:FusionCache:EagerRefreshRatio"] = "0",
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

    [Fact]
    public async Task GetOrSetEntity_ThenUrlGetOrSet_OnSameRequest_AreIndependentKeys()
    {
        string domain = "fc-both-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = DomainBase(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCache:Enabled"] = "false";
        });

        int entityCalls = 0;
        int listCalls = 0;

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(config, a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainFusionCache cache) =>
            {
                cache.SetEntityIdentity(http, "items", "1");
                string? entity = await cache.GetOrSetEntityAsync(
                    http,
                    _ =>
                    {
                        Interlocked.Increment(ref entityCalls);
                        return Task.FromResult<string?>("e1");
                    },
                    http.RequestAborted);
                string list = await cache.GetOrSetAsync(
                    http,
                    _ =>
                    {
                        Interlocked.Increment(ref listCalls);
                        return Task.FromResult("list");
                    },
                    http.RequestAborted);
                return Results.Text(entity + "|" + list);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            r1.IsSuccessStatusCode.Should().BeTrue();
            b1.Should().Be("e1|list");
            x1.Should().Contain("dc=miss");
            Volatile.Read(ref entityCalls).Should().Be(1);
            Volatile.Read(ref listCalls).Should().Be(1);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            b2.Should().Be("e1|list");
            x2.Should().Contain("dc=hit");
            Volatile.Read(ref entityCalls).Should().Be(1);
            Volatile.Read(ref listCalls).Should().Be(1,
                "URL-shaped GetOrSet after entity GetOrSet must not stay on the :id: key");

            CacheInvalidationResult inv = await app.Services
                .GetRequiredService<ICacheOrchestratorInvalidator>()
                .InvalidateEntityAsync(domain, "items", "1", TestContext.Current.CancellationToken);
            inv.Succeeded.Should().BeTrue();

            (HttpResponseMessage r3, string _, string b3) = await GetAsync(client, "/x");
            b3.Should().Be("e1|list");
            Volatile.Read(ref entityCalls).Should().Be(2);
            Volatile.Read(ref listCalls).Should().Be(1, "entity invalidate must not evict the URL-shaped entry");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task FusionOnly_AcceptNormalization_RestoresOriginalRequestHeader()
    {
        string domain = "fc-accept-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = DomainBase(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCache:Enabled"] = "false";
            d[$"Cache:Domains:{domain}:VaryByAccept"] = "true";
            d[$"Cache:Domains:{domain}:AcceptNormalizationList:0"] = "application/json";
        });

        const string original = "application/json, text/html;q=0.8";

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(config, a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainFusionCache cache, FactoryCounter factory) =>
            {
                string cached = await cache.GetOrSetAsync(http, _ =>
                {
                    factory.Increment();
                    return Task.FromResult("payload");
                }, http.RequestAborted);
                string accept = http.Request.Headers.Accept.ToString();
                return Results.Text(accept + "|" + cached);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            Dictionary<string, string> headers = new() { ["Accept"] = original };

            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x", headers);
            r1.IsSuccessStatusCode.Should().BeTrue();
            x1.Should().Contain("dc=miss");
            b1.Should().EndWith("|payload");
            b1.Should().Contain("text/html",
                "Fusion key generation must restore Accept after prefer-list collapse");
            b1.Should().NotStartWith("application/json|",
                "handler must not see the collapsed prefer-list value");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x", headers);
            x2.Should().Contain("dc=hit");
            b2.Should().EndWith("|payload");
            b2.Should().Contain("text/html");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExplicitDomainOverload_Http_CachesWithoutOutputCachePolicy()
    {
        string domain = "fc-exp-" + Guid.NewGuid().ToString("N");

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(DomainBase(domain), a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainFusionCache cache, FactoryCounter factory) =>
            {
                string value = await cache.GetOrSetAsync(http, domain, _ =>
                {
                    factory.Increment();
                    return Task.FromResult("explicit");
                }, http.RequestAborted);
                return Results.Text(value);
            });
        });

        try
        {
            (HttpResponseMessage r1, string _, string b1) = await GetAsync(client, "/x");
            r1.IsSuccessStatusCode.Should().BeTrue();
            b1.Should().Be("explicit");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            (HttpResponseMessage r2, string _, string b2) = await GetAsync(client, "/x");
            b2.Should().Be("explicit");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1,
                "Fusion-only explicit domain must cache without endpoint OC metadata");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExplicitFusionDomain_WinsOverOutputCacheDomainOnSameRequest()
    {
        string ocDomain = "oc-snap-" + Guid.NewGuid().ToString("N");
        string fcDomain = "fc-snap-" + Guid.NewGuid().ToString("N");

        Dictionary<string, string?> config = DomainBase(ocDomain, d =>
        {
            d[$"Cache:Domains:{ocDomain}:OutputCache:Enabled"] = "false";
        });
        foreach (KeyValuePair<string, string?> kv in DomainBase(fcDomain, d =>
        {
            d[$"Cache:Domains:{fcDomain}:OutputCache:Enabled"] = "false";
        }))
        {
            config[kv.Key] = kv.Value;
        }

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(config, a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainFusionCache cache, FactoryCounter factory) =>
            {
                // Policy pins ocDomain on Items; explicit Fusion name must replace it.
                string value = await cache.GetOrSetAsync(http, fcDomain, _ =>
                {
                    factory.Increment();
                    return Task.FromResult("from-fc");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(ocDomain);
        });

        try
        {
            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x");
            b1.Should().Be("from-fc");
            x1.Should().Contain($"domain={ocDomain}");
            x1.Should().Contain("dc=miss");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x");
            b2.Should().Be("from-fc");
            x2.Should().Contain("dc=hit");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task DataCacheRespectAuthBypass_True_Authorization_RunsFactoryEveryTime()
    {
        string domain = "fc-auth-on-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = DomainBase(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCache:Enabled"] = "false";
            d[$"Cache:Domains:{domain}:DataCacheRespectAuthBypass"] = "true";
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
            Dictionary<string, string> auth = new() { ["Authorization"] = "Bearer token" };

            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x", auth);
            b1.Should().Be("n1");
            x1.Should().Contain("dc=bypass");

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x", auth);
            b2.Should().Be("n2");
            x2.Should().Contain("dc=bypass");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(2);
            r1.IsSuccessStatusCode.Should().BeTrue();
            r2.IsSuccessStatusCode.Should().BeTrue();
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task DataCacheRespectAuthBypass_False_StillCachesUnderAuthorization()
    {
        string domain = "fc-auth-off-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?> config = DomainBase(domain, d =>
        {
            d[$"Cache:Domains:{domain}:OutputCache:Enabled"] = "false";
            d[$"Cache:Domains:{domain}:DataCacheRespectAuthBypass"] = "false";
        });

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(config, a =>
        {
            a.MapGet("/x", async (HttpContext http, IDomainFusionCache cache, FactoryCounter factory) =>
            {
                string value = await cache.GetOrSetAsync(http, _ =>
                {
                    factory.Increment();
                    return Task.FromResult("secret");
                }, http.RequestAborted);
                return Results.Text(value);
            }).CacheOutputWithDomain(domain);
        });

        try
        {
            Dictionary<string, string> auth = new() { ["Authorization"] = "Bearer token" };

            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x", auth);
            b1.Should().Be("secret");
            x1.Should().Contain("dc=miss");

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x", auth);
            b2.Should().Be("secret");
            x2.Should().Contain("dc=hit");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(1);
            r1.IsSuccessStatusCode.Should().BeTrue();
            r2.IsSuccessStatusCode.Should().BeTrue();
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task RequestNoStore_BypassesOutputAndFusion()
    {
        string domain = "fc-ns-" + Guid.NewGuid().ToString("N");

        (HttpClient? client, WebApplication? app) = await StartHttpAsync(DomainBase(domain), a =>
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
            Dictionary<string, string> noStore = new() { ["Cache-Control"] = "no-store" };

            (HttpResponseMessage r1, string x1, string b1) = await GetAsync(client, "/x", noStore);
            b1.Should().Be("n1");
            x1.Should().Contain("oc=bypass");
            x1.Should().Contain("dc=bypass");
            x1.Should().Contain("fa=run");

            (HttpResponseMessage r2, string x2, string b2) = await GetAsync(client, "/x", noStore);
            b2.Should().Be("n2");
            x2.Should().Contain("dc=bypass");
            app.Services.GetRequiredService<FactoryCounter>().Count.Should().Be(2);
            r1.IsSuccessStatusCode.Should().BeTrue();
            r2.IsSuccessStatusCode.Should().BeTrue();
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
