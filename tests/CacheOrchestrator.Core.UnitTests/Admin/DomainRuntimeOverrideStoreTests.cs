using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Core.UnitTests.Admin;

public class DomainRuntimeOverrideStoreTests
{
    [Fact]
    public void SetVersion_RebuildsDomainOptions_WithNewVersion()
    {
        DomainRuntimeOverrideStore store = new();
        using DomainCacheOptionsProvider provider = CreateProvider(store, new CacheOrchestratorOptions
        {
            Domains =
            {
                ["catalog"] = new()
                {
                    Version = "v1",
                    OutputCache = new() { TtlSeconds = 60 }
                }
            }
        });

        DomainCacheOptions before = provider.GetOrCreateDomainOptions("catalog");
        before.Version.Should().Be("v1");
        string hexBefore = before.VersionHex;

        store.SetVersion("catalog", "v2");

        DomainCacheOptions after = provider.GetOrCreateDomainOptions("catalog");
        after.Should().NotBeSameAs(before);
        after.Version.Should().Be("v2");
        after.VersionHex.Should().NotBe(hexBefore);
        after.OutputTtl.Should().Be(TimeSpan.FromSeconds(60));
        store.Get("catalog")!.Version.Should().Be("v2");
    }

    [Fact]
    public void PatchSettings_OverridesOnlyProvidedFields()
    {
        DomainRuntimeOverrideStore store = new();
        using DomainCacheOptionsProvider provider = CreateProvider(store, new CacheOrchestratorOptions
        {
            Domains =
            {
                ["catalog"] = new()
                {
                    Version = "v1",
                    OutputCache = new() { TtlSeconds = 60 },
                    DataCache = new() { TtlSeconds = 100 },
                    ClientCache = new() { TtlSeconds = 30 }
                }
            }
        });

        store.PatchSettings("catalog", new DomainSettingsPatch
        {
            OutputCacheTtl = TimeSpan.FromSeconds(120),
            ClientTtl = TimeSpan.FromSeconds(15)
        });

        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("catalog");
        opts.Version.Should().Be("v1");
        opts.OutputTtl.Should().Be(TimeSpan.FromSeconds(120));
        opts.DataCacheTtl.Should().Be(TimeSpan.FromSeconds(100));
        opts.ClientTtlSeconds.Should().Be(15);
    }

    [Fact]
    public void SetVersion_PreservesExistingTtlOverrides()
    {
        DomainRuntimeOverrideStore store = new();
        using DomainCacheOptionsProvider provider = CreateProvider(store, new CacheOrchestratorOptions
        {
            Domains =
            {
                ["catalog"] = new()
                {
                    Version = "v1",
                    OutputCache = new() { TtlSeconds = 60 }
                }
            }
        });

        store.PatchSettings("catalog", new DomainSettingsPatch { OutputCacheTtl = TimeSpan.FromSeconds(99) });
        store.SetVersion("catalog", "v9");

        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("catalog");
        opts.Version.Should().Be("v9");
        opts.OutputTtl.Should().Be(TimeSpan.FromSeconds(99));
    }

    [Fact]
    public void ConfigReload_StillAppliesRuntimeOverlay()
    {
        DomainRuntimeOverrideStore store = new();
        TestOptionsMonitor monitor = new(new CacheOrchestratorOptions
        {
            Domains =
            {
                ["catalog"] = new()
                {
                    Version = "cfg-1",
                    OutputCache = new() { TtlSeconds = 10 }
                }
            }
        });
        using DomainCacheOptionsProvider provider = new(
            monitor,
            NullLogger<DomainCacheOptionsProvider>.Instance,
            store);

        store.SetVersion("catalog", "rt-1");
        provider.GetOrCreateDomainOptions("catalog").Version.Should().Be("rt-1");

        monitor.TriggerChange(new CacheOrchestratorOptions
        {
            Domains =
            {
                ["catalog"] = new()
                {
                    Version = "cfg-2",
                    OutputCache = new() { TtlSeconds = 20 }
                }
            }
        });

        DomainCacheOptions after = provider.GetOrCreateDomainOptions("catalog");
        after.Version.Should().Be("rt-1", "runtime overlay wins over reloaded config");
        after.OutputTtl.Should().Be(TimeSpan.FromSeconds(20));
    }

    private static DomainCacheOptionsProvider CreateProvider(
        IDomainRuntimeOverrideStore store,
        CacheOrchestratorOptions options)
    {
        return new DomainCacheOptionsProvider(
            new TestOptionsMonitor(options),
            NullLogger<DomainCacheOptionsProvider>.Instance,
            store);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<CacheOrchestratorOptions>
    {
        private event Action<CacheOrchestratorOptions, string?>? _onChange;

        public TestOptionsMonitor(CacheOrchestratorOptions current)
        {
            CurrentValue = current;
        }

        public CacheOrchestratorOptions CurrentValue { get; private set; }
        public CacheOrchestratorOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<CacheOrchestratorOptions, string?> listener)
        {
            _onChange += listener;
            return new Subscription(() => _onChange -= listener);
        }

        public void TriggerChange(CacheOrchestratorOptions newOptions)
        {
            CurrentValue = newOptions;
            _onChange?.Invoke(newOptions, null);
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
