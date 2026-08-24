using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.IntegrationTests.Fusion;

/// <summary>
/// Integration coverage for Fusion fail-safe: after soft TTL expires, a failing factory
/// must still return the last good value and mark disposition as <see cref="DataCacheResult.Stale"/>.
/// </summary>
public class FusionCacheFailSafeStaleTests
{
    private static ServiceProvider BuildProvider()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
                ["Cache:Domains:stale:Version"] = "v1",
                // Soft-expire quickly so the next GetOrSet re-runs the factory.
                ["Cache:Domains:stale:DataCache:Ttl"] = "00:00:01",
                ["Cache:Domains:stale:FusionCache:HardTtl"] = "01:00:00",
                // Fail-safe window must outlive soft TTL (IsFailSafeEnabled when > 0).
                ["Cache:Domains:stale:FusionCache:FailSafe"] = "1.00:00:00",
                // Disable jitter / eager refresh so expiry timing is predictable.
                ["Cache:Domains:stale:FusionCache:Jitter"] = "00:00:00",
                ["Cache:Domains:stale:FusionCache:EagerRefreshRatio"] = "0",
                ["Cache:Domains:stale:FusionCache:FactorySoftTimeout"] = "00:00:05",
                ["Cache:Domains:stale:FusionCache:FactoryHardTimeout"] = "00:00:10",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateHttp(IDomainCacheOptionsProvider domains, string path)
    {
        DefaultHttpContext http = new();
        http.Request.Method = "GET";
        http.Request.Path = path;
        domains.EnsureDomainOptions(http, "stale");
        return http;
    }

    private static CacheDisposition? GetDisposition(HttpContext http) =>
        http.Features.Get<ICacheOrchestratorFeature>()?.Disposition as CacheDisposition;

    [Fact]
    public async Task GetOrSetAsync_WhenFactoryFailsAfterSoftExpiry_ReturnsStaleValueAndDisposition()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IDomainCacheOptionsProvider domains = sp.GetRequiredService<IDomainCacheOptionsProvider>();

        // --- Seed: successful factory → MISS ---
        DefaultHttpContext http1 = CreateHttp(domains, "/api/stale/item");
        int calls = 0;

        string seeded = await cache.GetOrSetAsync(http1, _ =>
        {
            calls++;
            return Task.FromResult("good-value");
        }, TestContext.Current.CancellationToken);

        seeded.Should().Be("good-value");
        calls.Should().Be(1);
        GetDisposition(http1)!.Data.Should().Be(DataCacheResult.Miss);

        // Wait past soft TTL (1s) so Fusion considers the entry expired and re-invokes factory.
        await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);

        // --- Refresh fails: fail-safe should serve last good value ---
        DefaultHttpContext http2 = CreateHttp(domains, "/api/stale/item");

        string served = await cache.GetOrSetAsync<string>(http2, _ =>
        {
            calls++;
            throw new InvalidOperationException("upstream unavailable");
        }, TestContext.Current.CancellationToken);

        served.Should().Be("good-value", "fail-safe must return the previously cached value");
        calls.Should().Be(2, "factory must run again after soft expiry");

        CacheDisposition? disp = GetDisposition(http2);
        disp.Should().NotBeNull();
        disp!.Data.Should().Be(DataCacheResult.Stale);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenFactoryFailsWithNoPriorValue_Throws()
    {
        await using ServiceProvider sp = BuildProvider();
        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IDomainCacheOptionsProvider domains = sp.GetRequiredService<IDomainCacheOptionsProvider>();

        DefaultHttpContext http = CreateHttp(domains, "/api/stale/cold");

        Func<Task> act = async () =>
            await cache.GetOrSetAsync<string>(http, _ =>
                throw new InvalidOperationException("no cached value to fall back on"),
                TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no cached value*");
    }

    [Fact]
    public async Task GetOrSetAsync_WhenFailSafeDisabled_FactoryFailureAfterExpiry_Throws()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:FusionCacheInstances:default:Provider"] = "InMemory",
                ["Cache:Domains:nofail:Version"] = "v1",
                ["Cache:Domains:nofail:DataCache:Ttl"] = "00:00:01",
                ["Cache:Domains:nofail:FusionCache:HardTtl"] = "01:00:00",
                // Zero fail-safe → IsFailSafeEnabled = false
                ["Cache:Domains:nofail:FusionCache:FailSafe"] = "00:00:00",
                ["Cache:Domains:nofail:FusionCache:Jitter"] = "00:00:00",
                ["Cache:Domains:nofail:FusionCache:EagerRefreshRatio"] = "0",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestrator(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IDomainFusionCache cache = sp.GetRequiredService<IDomainFusionCache>();
        IDomainCacheOptionsProvider domains = sp.GetRequiredService<IDomainCacheOptionsProvider>();

        DefaultHttpContext seedHttp = new();
        seedHttp.Request.Method = "GET";
        seedHttp.Request.Path = "/api/nofail/1";
        domains.EnsureDomainOptions(seedHttp, "nofail");

        await cache.GetOrSetAsync(seedHttp, _ => Task.FromResult("seed"), TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(1200), TestContext.Current.CancellationToken);

        DefaultHttpContext failHttp = new();
        failHttp.Request.Method = "GET";
        failHttp.Request.Path = "/api/nofail/1";
        domains.EnsureDomainOptions(failHttp, "nofail");

        Func<Task> act = async () =>
            await cache.GetOrSetAsync<string>(failHttp, _ =>
                throw new InvalidOperationException("boom"),
                TestContext.Current.CancellationToken);

        // Without fail-safe there is no stale serve after expiry — exception surfaces.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
