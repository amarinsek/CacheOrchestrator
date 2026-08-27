using CacheOrchestrator.Backends;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.FusionCache.Backends;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.AspNetCore.UnitTests.Backends;

/// <summary>
/// End-to-end proof that a custom <see cref="ICacheBackendRegistrar"/> can supply Fusion L2
/// (keyed <see cref="IDistributedCache"/>) and that values survive a new host / empty L1.
/// </summary>
public class CustomBackendEndToEndTests
{
    private const string ProviderName = "FakeDb";

    /// <summary>
    /// Minimal in-process distributed cache shared across DI containers to simulate L2.
    /// </summary>
    private sealed class DictionaryDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.Ordinal);

        public int GetCount;
        public int SetCount;

        public byte[]? Get(string key)
        {
            Interlocked.Increment(ref GetCount);
            return _store.TryGetValue(key, out byte[]? value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Interlocked.Increment(ref SetCount);
            _store[key] = value;
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
            // no-op (entries are immortal for this test double)
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _store.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Fusion-only custom backend (like a SQL L2 with no Output Cache store).
    /// </summary>
    private sealed class FakeDbBackendRegistrar : ICacheBackendRegistrar, IFusionCacheBackendRegistrar
    {
        private readonly ConcurrentDictionary<string, DictionaryDistributedCache> _cachesByInstance;

        public FakeDbBackendRegistrar(ConcurrentDictionary<string, DictionaryDistributedCache> cachesByInstance)
        {
            _cachesByInstance = cachesByInstance;
        }

        public string Name => ProviderName;
        public bool SupportsOutputCacheStore => false;

        public void RegisterOutputCache(OutputCacheRegistrationContext context) =>
            throw new InvalidOperationException($"{ProviderName} does not support Output Cache.");

        public void RegisterFusionCache(FusionCacheRegistrationContext context)
        {
            DictionaryDistributedCache cache = _cachesByInstance.GetOrAdd(
                context.InstanceName,
                static _ => new DictionaryDistributedCache());

            context.Services.TryAddKeyedSingleton<IDistributedCache>(context.InstanceName, cache);
            context.FusionBuilder.WithRegisteredKeyedDistributedCache(context.InstanceName);
        }

        public void RegisterHealthProbes(BackendHealthRegistrationContext context)
        {
        }

        public void RegisterHealthProbes(FusionBackendHealthRegistrationContext context)
        {
            context.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ICacheOrchestratorHealthProbe, FakeDbHealthProbe>());
        }
    }

    private sealed class FakeDbHealthProbe : ICacheOrchestratorHealthProbe
    {
        public string Name => "fake-db";

        public Task ProbeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static IConfigurationRoot BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Namespace"] = "custom-e2e",
                ["Cache:OutputCache:Provider"] = "InMemory",
                ["Cache:DataCacheInstances:default:Provider"] = ProviderName,
                ["Cache:Domains:catalog:Version"] = "v1",
                ["Cache:Domains:catalog:DataCache:TtlSeconds"] = "120",
                // Keep L2 writes on the request path so the second host can read them reliably.
                ["Cache:Domains:catalog:FusionCache:AllowBackgroundDistributed"] = "false",
            })
            .Build();

    private static ServiceProvider BuildHost(
        ConcurrentDictionary<string, DictionaryDistributedCache> sharedL2)
    {
        IConfigurationRoot config = BuildConfig();
        FakeDbBackendRegistrar registrar = new(sharedL2);
        ServiceCollection services = new();
        services.AddLogging();
        // FakeDb is Fusion L2 only; InMemory remains the Output Cache provider.
        services.AddCacheOrchestratorAspNetCore(config);
        services.AddFusionCacheBackend(registrar);
        services.AddCacheOrchestratorFusionCache(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CustomBackend_GetOrSet_PopulatesSharedL2_AndSecondHostHitsWithoutFactory()
    {
        var sharedL2 = new ConcurrentDictionary<string, DictionaryDistributedCache>(StringComparer.OrdinalIgnoreCase);

        // --- Host A: cold L1 + empty L2 â†’ factory once, write L2 ---
        await using ServiceProvider hostA = BuildHost(sharedL2);
        IDomainDataCache cacheA = hostA.GetRequiredService<IDomainDataCache>();
        IRequestDomainCacheOptions domainsA = hostA.GetRequiredService<IRequestDomainCacheOptions>();

        DefaultHttpContext httpA = new();
        httpA.Request.Method = "GET";
        httpA.Request.Path = "/api/catalog/42";
        domainsA.EnsureDomainOptions(httpA, "catalog");

        int factoryA = 0;
        string valueA = await cacheA.GetOrSetAsync(httpA, "catalog", _ =>
        {
            factoryA++;
            return Task.FromResult("catalog-payload");
        }, TestContext.Current.CancellationToken);

        valueA.Should().Be("catalog-payload");
        factoryA.Should().Be(1);

        sharedL2.Should().ContainKey("default");
        DictionaryDistributedCache l2 = sharedL2["default"];
        l2.SetCount.Should().BeGreaterThan(0, "custom L2 must receive at least one Set from FusionCache");

        // --- Host B: new process-like DI (empty L1), same shared L2 dictionary ---
        await using ServiceProvider hostB = BuildHost(sharedL2);
        IDomainDataCache cacheB = hostB.GetRequiredService<IDomainDataCache>();
        IRequestDomainCacheOptions domainsB = hostB.GetRequiredService<IRequestDomainCacheOptions>();

        DefaultHttpContext httpB = new();
        httpB.Request.Method = "GET";
        httpB.Request.Path = "/api/catalog/42";
        domainsB.EnsureDomainOptions(httpB, "catalog");

        int factoryB = 0;
        int getsBefore = l2.GetCount;

        string valueB = await cacheB.GetOrSetAsync(httpB, "catalog", _ =>
        {
            factoryB++;
            return Task.FromResult("should-not-run");
        }, TestContext.Current.CancellationToken);

        valueB.Should().Be("catalog-payload");
        factoryB.Should().Be(0, "value must come from custom L2, not a new factory run");
        l2.GetCount.Should().BeGreaterThan(getsBefore, "custom L2 Get should run on the second host");
    }

    [Fact]
    public void CustomBackend_RegistersAndHealthProbeIsDiscoverable()
    {
        var sharedL2 = new ConcurrentDictionary<string, DictionaryDistributedCache>(StringComparer.OrdinalIgnoreCase);
        using ServiceProvider sp = BuildHost(sharedL2);

        IEnumerable<ICacheOrchestratorHealthProbe> probes =
            sp.GetServices<ICacheOrchestratorHealthProbe>();

        probes.Should().Contain(p => p.Name == "fake-db");
    }

    [Fact]
    public void CustomBackend_CannotBeUsedAsOutputCacheProvider()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:Provider"] = ProviderName,
                ["Cache:DataCacheInstances:default:Provider"] = "InMemory"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var shared = new ConcurrentDictionary<string, DictionaryDistributedCache>(StringComparer.OrdinalIgnoreCase);

        Func<IServiceCollection> act = () => services.AddCacheOrchestratorAspNetCore(
            config,
            o => o.AddBackend(new FakeDbBackendRegistrar(shared)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not support an Output Cache store*");
    }
}
