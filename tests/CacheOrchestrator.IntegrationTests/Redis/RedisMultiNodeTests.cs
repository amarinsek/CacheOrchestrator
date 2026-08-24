using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.IntegrationTests.Infrastructure;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using CacheOrchestrator.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.Redis;

/// <summary>
/// F — multi-node style Redis scenarios (shared OC store, shared FC L2, health probe).
/// Uses the shared <see cref="RedisFixture"/> Testcontainer.
/// </summary>
[Collection("Redis")]
public class RedisMultiNodeTests
{
    private readonly RedisFixture _redis;

    public RedisMultiNodeTests(RedisFixture redis) => _redis = redis;

    private sealed class HitCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private static Dictionary<string, string?> RedisConfig(
        string domain,
        string redisCs,
        string ocProvider,
        string fcProvider)
        => new()
        {
            ["Cache:Namespace"] = "it-multi-" + Guid.NewGuid().ToString("N")[..8],
            ["Cache:OutputCache:Provider"] = ocProvider,
            ["Cache:DataCacheInstances:default:Provider"] = fcProvider,
            ["Cache:Redis:Configuration"] = redisCs,
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientCache:Ttl"] = "00:01:00",
            [$"Cache:Domains:{domain}:ClientCache:TtlMin"] = "00:01:00",
            [$"Cache:Domains:{domain}:OutputCache:Ttl"] = "00:02:00",
            [$"Cache:Domains:{domain}:DataCache:Ttl"] = "00:05:00",
            [$"Cache:Domains:{domain}:FusionCache:Jitter"] = "00:00:00",
        };

    private static async Task<(HttpClient Client, WebApplication App, HitCounter Hits)> StartHostAsync(
        Dictionary<string, string?> configValues,
        string domain,
        string path)
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
        builder.Services.AddCacheOrchestrator(config, o => o.AddRedisBackend());
        builder.Services.AddSingleton<HitCounter>();

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        HitCounter hits = app.Services.GetRequiredService<HitCounter>();
        app.MapGet(path, async (HttpContext http, IDomainFusionCache cache, HitCounter h) =>
        {
            h.Increment();
            string value = await cache.GetOrSetAsync(http, _ => Task.FromResult("shared-" + domain), http.RequestAborted);
            return Results.Text(value);
        }).CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return (app.GetTestClient(), app, hits);
    }

    // -------------------------------------------------------------------------
    // F31 — two hosts, shared Redis Output Cache
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SharedRedisOutputCache_SecondHost_ServesHitWithoutRunningHandler()
    {
        string domain = "mn-oc-" + Guid.NewGuid().ToString("N");
        string path = "/" + domain;
        // Same Namespace so both hosts share the Redis OC keyspace.
        string ns = "shared-oc-" + Guid.NewGuid().ToString("N")[..8];
        Dictionary<string, string?> cfg = RedisConfig(domain, _redis.ConnectionString, "Redis", "InMemory");
        cfg["Cache:Namespace"] = ns;

        (HttpClient clientA, WebApplication appA, HitCounter hitsA) = await StartHostAsync(cfg, domain, path);
        (HttpClient clientB, WebApplication appB, HitCounter hitsB) = await StartHostAsync(cfg, domain, path);

        try
        {
            HttpResponseMessage r1 = await clientA.GetAsync(path, TestContext.Current.CancellationToken);
            r1.IsSuccessStatusCode.Should().BeTrue();
            (await r1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("shared-" + domain);
            hitsA.Count.Should().Be(1);

            // Host B: lookup shared Redis OC — handler on B must not run.
            HttpResponseMessage r2 = await clientB.GetAsync(path, TestContext.Current.CancellationToken);
            r2.IsSuccessStatusCode.Should().BeTrue();
            (await r2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("shared-" + domain);
            hitsB.Count.Should().Be(0, "Output Cache entry in Redis must be shared across hosts");

            string x2 = r2.Headers.TryGetValues("X-Cache", out IEnumerable<string>? xv)
                ? string.Join(",", xv)
                : "";
            x2.Should().Contain("oc=hit");
        }
        finally
        {
            await appA.StopAsync(TestContext.Current.CancellationToken);
            await appA.DisposeAsync();
            await appB.StopAsync(TestContext.Current.CancellationToken);
            await appB.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // F32 — two DI hosts, shared Fusion L2 (empty L1 on second)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SharedRedisFusionL2_SecondHost_HitsWithoutFactory()
    {
        string domain = "mn-fc-" + Guid.NewGuid().ToString("N");
        string ns = "shared-fc-" + Guid.NewGuid().ToString("N")[..8];

        async Task<ServiceProvider> BuildHostAsync()
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cache:Namespace"] = ns,
                    ["Cache:OutputCache:Provider"] = "InMemory",
                    ["Cache:DataCacheInstances:default:Provider"] = "Redis",
                    ["Cache:Redis:Configuration"] = _redis.ConnectionString,
                    [$"Cache:Domains:{domain}:Version"] = "v1",
                    [$"Cache:Domains:{domain}:DataCache:Ttl"] = "00:05:00",
                    [$"Cache:Domains:{domain}:FusionCache:Jitter"] = "00:00:00",
                })
                .Build();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddCacheOrchestrator(config, o => o.AddRedisBackend());
            return services.BuildServiceProvider();
        }

        await using ServiceProvider spA = await BuildHostAsync();
        await using ServiceProvider spB = await BuildHostAsync();

        IDomainFusionCache cacheA = spA.GetRequiredService<IDomainFusionCache>();
        IDomainFusionCache cacheB = spB.GetRequiredService<IDomainFusionCache>();
        IRequestDomainCacheOptions domainsA = spA.GetRequiredService<IRequestDomainCacheOptions>();
        IRequestDomainCacheOptions domainsB = spB.GetRequiredService<IRequestDomainCacheOptions>();

        int factoryA = 0;
        int factoryB = 0;

        DefaultHttpContext httpA = new();
        httpA.Request.Method = "GET";
        httpA.Request.Path = "/api/shared-l2";
        domainsA.EnsureDomainOptions(httpA, domain);

        string vA = await cacheA.GetOrSetAsync(httpA, _ =>
        {
            Interlocked.Increment(ref factoryA);
            return Task.FromResult("l2-value");
        }, TestContext.Current.CancellationToken);

        vA.Should().Be("l2-value");
        factoryA.Should().Be(1);

        // Allow distributed write to settle
        await Task.Delay(200, TestContext.Current.CancellationToken);

        DefaultHttpContext httpB = new();
        httpB.Request.Method = "GET";
        httpB.Request.Path = "/api/shared-l2";
        domainsB.EnsureDomainOptions(httpB, domain);

        string vB = await cacheB.GetOrSetAsync(httpB, _ =>
        {
            Interlocked.Increment(ref factoryB);
            return Task.FromResult("should-not-run");
        }, TestContext.Current.CancellationToken);

        vB.Should().Be("l2-value");
        factoryB.Should().Be(0, "host B must hydrate from shared Redis L2 without running factory");
    }

    // -------------------------------------------------------------------------
    // F33 — invalidate on A → subsequent B miss (L2 + backplane / tag eviction)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvalidateDomain_OnHostA_CausesMissOnHostB()
    {
        string domain = "mn-inv-" + Guid.NewGuid().ToString("N");
        string ns = "shared-inv-" + Guid.NewGuid().ToString("N")[..8];

        async Task<ServiceProvider> BuildHostAsync()
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cache:Namespace"] = ns,
                    ["Cache:OutputCache:Provider"] = "InMemory",
                    ["Cache:DataCacheInstances:default:Provider"] = "Redis",
                    ["Cache:Redis:Configuration"] = _redis.ConnectionString,
                    [$"Cache:Domains:{domain}:Version"] = "v1",
                    [$"Cache:Domains:{domain}:DataCache:Ttl"] = "00:05:00",
                    [$"Cache:Domains:{domain}:FusionCache:Jitter"] = "00:00:00",
                })
                .Build();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddCacheOrchestrator(config, o => o.AddRedisBackend());
            return services.BuildServiceProvider();
        }

        await using ServiceProvider spA = await BuildHostAsync();
        await using ServiceProvider spB = await BuildHostAsync();

        IDomainFusionCache cacheA = spA.GetRequiredService<IDomainFusionCache>();
        IDomainFusionCache cacheB = spB.GetRequiredService<IDomainFusionCache>();
        IRequestDomainCacheOptions domainsA = spA.GetRequiredService<IRequestDomainCacheOptions>();
        IRequestDomainCacheOptions domainsB = spB.GetRequiredService<IRequestDomainCacheOptions>();

        DefaultHttpContext MakeHttp(IRequestDomainCacheOptions domains)
        {
            DefaultHttpContext http = new();
            http.Request.Method = "GET";
            http.Request.Path = "/api/inv";
            domains.EnsureDomainOptions(http, domain);
            return http;
        }

        int factoryB = 0;

        await cacheA.GetOrSetAsync(MakeHttp(domainsA), _ => Task.FromResult("seed"), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Populate B L1 from L2
        await cacheB.GetOrSetAsync(MakeHttp(domainsB), _ =>
        {
            Interlocked.Increment(ref factoryB);
            return Task.FromResult("unexpected");
        }, TestContext.Current.CancellationToken);
        factoryB.Should().Be(0);

        await spA.GetRequiredService<ICacheOrchestratorInvalidator>()
            .InvalidateDomainAsync(domain, TestContext.Current.CancellationToken);

        // Backplane + L2 tag removal: B should miss after a short settle.
        await Task.Delay(300, TestContext.Current.CancellationToken);

        string after = await cacheB.GetOrSetAsync(MakeHttp(domainsB), _ =>
        {
            Interlocked.Increment(ref factoryB);
            return Task.FromResult("rebuilt");
        }, TestContext.Current.CancellationToken);

        after.Should().Be("rebuilt");
        factoryB.Should().Be(1, "invalidation on A must force B to re-run factory (L2/backplane)");
    }

    // -------------------------------------------------------------------------
    // F34 — health check with real Redis probe
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HealthCheck_WithRedisBackend_ProbesSucceed()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "Redis",
                ["Cache:DataCacheInstances:default:Provider"] = "Redis",
                ["Cache:Redis:Configuration"] = _redis.ConnectionString,
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddRedisBackend());
        services.AddHealthChecks().AddCacheOrchestrator();

        await using ServiceProvider sp = services.BuildServiceProvider();

        // Probes registered by Redis registrar
        IEnumerable<ICacheOrchestratorHealthProbe> probes = sp.GetServices<ICacheOrchestratorHealthProbe>();
        probes.Should().NotBeEmpty();

        HealthCheckService health = sp.GetRequiredService<HealthCheckService>();
        HealthReport report = await health.CheckHealthAsync(TestContext.Current.CancellationToken);

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Keys.Should().Contain(k => k.Contains("cache", StringComparison.OrdinalIgnoreCase)
            || k.Contains("Cache", StringComparison.Ordinal));
    }
}
