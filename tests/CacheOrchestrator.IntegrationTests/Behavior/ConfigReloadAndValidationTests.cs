using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
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

/// <summary>
/// E — Config reload (TTL) and options validation failures.
/// </summary>
public class ConfigReloadAndValidationTests
{
    [Fact]
    public async Task ClientTtl_Reload_OnRunningHost_UpdatesCacheControl()
    {
        string domain = "cfg-ttl-" + Guid.NewGuid().ToString("N");
        var initial = new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientCache:TtlSeconds"] = "42",
            [$"Cache:Domains:{domain}:ClientCache:TtlMinSeconds"] = "42",
            [$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = "1",
            [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "300",
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
        builder.Services.AddCacheOrchestratorAspNetCore(config);
        builder.Services.AddCacheOrchestratorFusionCache(config);

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/x", () => Results.Text("body")).CacheOutputWithDomain(domain);

        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();

        try
        {
            HttpResponseMessage r1 = await client.GetAsync("/x", TestContext.Current.CancellationToken);
            string cc1 = GetCacheControl(r1);
            cc1.Should().Contain("max-age=42");

            reloadSource.Provider!.SetAndReload($"Cache:Domains:{domain}:ClientCache:TtlSeconds", "77");
            reloadSource.Provider.SetAndReload($"Cache:Domains:{domain}:ClientCache:TtlMinSeconds", "77");
            await WaitForClientTtlAsync(app.Services, domain, 77);

            // Expire OC entry so response headers are regenerated from new options.
            await Task.Delay(1100, TestContext.Current.CancellationToken);

            HttpResponseMessage r2 = await client.GetAsync("/x", TestContext.Current.CancellationToken);
            string cc2 = GetCacheControl(r2);
            cc2.Should().Contain("max-age=77");
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task MidRequest_Reload_DoesNotMutatePinnedDomainOptionsOnHttpContext()
    {
        // Integration-level confirmation of L1 Items pin (unit also covers this).
        // Use real provider via DI + reloadable config for end-to-end monitor path.
        var data = new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:Domains:pin:Version"] = "1",
            ["Cache:Domains:pin:ClientCache:TtlSeconds"] = "10",
            ["Cache:Domains:pin:ClientCache:TtlMinSeconds"] = "10",
        };
        var reloadSource = new ReloadableMemoryConfigurationSource(data);
        IConfigurationRoot config = new ConfigurationBuilder().Add(reloadSource).Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddCacheOrchestratorAspNetCore(config);
        services.AddCacheOrchestratorFusionCache(config);
        await using ServiceProvider sp = services.BuildServiceProvider();

        IRequestDomainCacheOptions domains = sp.GetRequiredService<IRequestDomainCacheOptions>();
        DefaultHttpContext http = new();
        DomainCacheOptions pinned = domains.EnsureDomainOptions(http, "pin");
        pinned.Version.Should().Be("1");
        pinned.ClientTtlSeconds.Should().Be(10);

        reloadSource.Provider!.SetAndReload("Cache:Domains:pin:Version", "2");
        reloadSource.Provider.SetAndReload("Cache:Domains:pin:ClientCache:TtlSeconds", "99");

        // Force options rebind
        _ = sp.GetRequiredService<IOptionsMonitor<CacheOrchestratorOptions>>().CurrentValue;
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _ = sp.GetRequiredService<IOptionsMonitor<CacheOrchestratorOptions>>().CurrentValue;

        DomainCacheOptions stillPinned = domains.GetDomainOptions(http)!;
        stillPinned.Should().BeSameAs(pinned);
        stillPinned.Version.Should().Be("1");
        stillPinned.ClientTtlSeconds.Should().Be(10);

        DomainCacheOptions fresh = domains.EnsureDomainOptions(new DefaultHttpContext(), "pin");
        // After global cache clear, new request sees reloaded values (may need poll)
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline && fresh.Version != "2")
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            _ = sp.GetRequiredService<IOptionsMonitor<CacheOrchestratorOptions>>().CurrentValue;
            fresh = domains.GetOrCreateDomainOptions("pin");
        }

        fresh.Version.Should().Be("2");
        fresh.ClientTtlSeconds.Should().Be(99);
    }

    [Fact]
    public void UnknownProvider_ThrowsAtRegistration()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "NotARealProvider",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();

        Action act = () => services.AddCacheOrchestratorAspNetCore(config);
        services.AddCacheOrchestratorFusionCache(config);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NotARealProvider*");
    }

    [Fact]
    public async Task NegativeClientTtl_FailsHostStart()
    {
        string domain = "neg-ttl-" + Guid.NewGuid().ToString("N");
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
                [$"Cache:Domains:{domain}:Version"] = "v1",
                [$"Cache:Domains:{domain}:ClientCache:TtlSeconds"] = "-1",
                [$"Cache:Domains:{domain}:OutputCache:TtlSeconds"] = "60",
                [$"Cache:Domains:{domain}:DataCache:TtlSeconds"] = "60",
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

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/x", () => Results.Text("ok")).CacheOutputWithDomain(domain);

        try
        {
            Func<Task> act = async () =>
            {
                await app.StartAsync(TestContext.Current.CancellationToken);
                _ = app.Services.GetRequiredService<IOptions<CacheOrchestratorOptions>>().Value;
            };

            Exception? caught = null;
            try
            {
                await act();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            caught.Should().NotBeNull("negative ClientCache.TtlSeconds must fail IValidateOptions at host start");
            caught.ToString().Should().ContainEquivalentOf("ClientCache.TtlSeconds");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static string GetCacheControl(HttpResponseMessage res) =>
        res.Headers.TryGetValues("Cache-Control", out IEnumerable<string>? v)
            ? string.Join(",", v)
            : string.Empty;

    private static async Task WaitForClientTtlAsync(IServiceProvider services, string domain, int expected)
    {
        IRequestDomainCacheOptions domains = services.GetRequiredService<IRequestDomainCacheOptions>();
        IOptionsMonitor<CacheOrchestratorOptions> monitor =
            services.GetRequiredService<IOptionsMonitor<CacheOrchestratorOptions>>();

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            _ = monitor.CurrentValue;
            if (domains.GetOrCreateDomainOptions(domain).ClientTtlSeconds == expected)
                return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        domains.GetOrCreateDomainOptions(domain).ClientTtlSeconds.Should().Be(expected);
    }
}
