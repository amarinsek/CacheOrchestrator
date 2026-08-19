using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.UnitTests.Configuration;

public class DomainCacheConfigProviderTests
{
    // =========================
    // NormalizeDomain
    // =========================

    [Theory]
    [InlineData(null, "default")]
    [InlineData("", "default")]
    [InlineData("   ", "default")]
    [InlineData("Product-Catalog", "product-catalog")]
    [InlineData("PRODUCT_CATALOG", "product_catalog")]
    [InlineData("product@catalog", "product@catalog")]
    [InlineData("product:catalog", "product:catalog")]
    [InlineData("product--catalog", "product-catalog")]
    [InlineData("product---catalog", "product-catalog")]
    [InlineData("---product---", "product")]
    [InlineData("product!!!catalog", "product-catalog")]
    [InlineData("product   catalog", "product-catalog")]
    [InlineData("a", "a")]
    [InlineData("---", "default")]
    [InlineData("!!!", "default")]
    [InlineData("-a-", "a")]
    [InlineData("a-b-c", "a-b-c")]
    public void NormalizeDomain_ReturnsExpectedResult(string? input, string expected)
    {
        string result = DomainName.Normalize(input!);
        result.Should().Be(expected);
    }

    [Fact]
    public void NormalizeDomain_IsCaseInsensitiveAndStable()
    {
        string a = DomainName.Normalize("My-Domain");
        string b = DomainName.Normalize("my-domain");
        string c = DomainName.Normalize("MY-DOMAIN");

        a.Should().Be(b).And.Be(c).And.Be("my-domain");
    }

    // =========================
    // EnsureConfig / GetConfig
    // =========================

    [Fact]
    public void EnsureConfig_ReturnsSameInstance_WithinSameRequest()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions
        {
            Domains = { ["products"] = new() { OutputCacheTtlSeconds = 100 } }
        });
        var http = new DefaultHttpContext();

        var cfg1 = provider.EnsureDomainOptions(http, "products");
        var cfg2 = provider.EnsureDomainOptions(http, "products");

        cfg1.Should().BeSameAs(cfg2);
    }

    [Fact]
    public void EnsureConfig_DifferentDomainOnSameRequest_ReplacesSnapshot()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions
        {
            Domains =
            {
                ["products"] = new() { OutputCacheTtlSeconds = 100, Version = "p1" },
                ["catalog"] = new() { OutputCacheTtlSeconds = 200, Version = "c1" }
            }
        });
        var http = new DefaultHttpContext();

        DomainCacheOptions products = provider.EnsureDomainOptions(http, "products");
        DomainCacheOptions catalog = provider.EnsureDomainOptions(http, "catalog");

        products.Domain.Should().Be("products");
        catalog.Should().NotBeSameAs(products);
        catalog.Domain.Should().Be("catalog");
        catalog.OutputTtl.Should().Be(TimeSpan.FromSeconds(200));
        catalog.Version.Should().Be("c1");
        provider.GetDomainOptions(http).Should().BeSameAs(catalog);
    }

    [Fact]
    public void EnsureConfig_UsesDomainSpecificSettings()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions
        {
            DomainDefaults = new() { OutputCacheTtlSeconds = 60 },
            Domains = { ["products"] = new() { OutputCacheTtlSeconds = 120 } }
        });

        var cfg = provider.EnsureDomainOptions(new DefaultHttpContext(), "products");

        cfg.Domain.Should().Be("products");
        cfg.OutputTtl.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void EnsureConfig_FallsBackToDomainDefaults_WhenDomainMissing()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions
        {
            DomainDefaults = new()
            {
                OutputCacheTtlSeconds = 90,
                FusionCacheSoftTtlSeconds = 300
            }
        });

        var cfg = provider.EnsureDomainOptions(new DefaultHttpContext(), "unknown-domain");

        cfg.Domain.Should().Be("unknown-domain");
        cfg.OutputTtl.Should().Be(TimeSpan.FromSeconds(90));
        cfg.FusionCacheSoftTtl.Should().Be(TimeSpan.FromSeconds(300));
    }

    [Fact]
    public void EnsureConfig_NormalizesDomainName()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions());
        var cfg = provider.EnsureDomainOptions(new DefaultHttpContext(), "  Product-Catalog  ");

        cfg.Domain.Should().Be("product-catalog");
    }

    [Fact]
    public void EnsureConfig_UsesStableDefaultVersion_WhenNotConfigured()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions());
        var cfg = provider.EnsureDomainOptions(new DefaultHttpContext(), "products");

        cfg.Version.Should().Be("1");
    }

    [Fact]
    public void EnsureConfig_UsesConfiguredVersion()
    {
        const string version = "v2";

        var provider = CreateProvider(new CacheOrchestratorOptions
        {
            Domains = { ["products"] = new() { Version = version } }
        });

        var cfg = provider.EnsureDomainOptions(new DefaultHttpContext(), "products");

        cfg.Version.Should().Be(version);
    }

    [Fact]
    public void EnsureConfig_CachesPerDomain_AcrossDifferentHttpContexts()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions
        {
            Domains = { ["products"] = new() { OutputCacheTtlSeconds = 150 } }
        });

        var cfg1 = provider.EnsureDomainOptions(new DefaultHttpContext(), "products");
        var cfg2 = provider.EnsureDomainOptions(new DefaultHttpContext(), "products");

        cfg1.Should().BeSameAs(cfg2);
    }

    [Fact]
    public void GetConfig_ReturnsNull_WhenNotYetEnsured()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions());
        var cfg = provider.GetDomainOptions(new DefaultHttpContext());

        cfg.Should().BeNull();
    }

    [Fact]
    public void GetConfig_ReturnsConfig_AfterEnsureConfig()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions());
        var http = new DefaultHttpContext();

        provider.EnsureDomainOptions(http, "products");
        var cfg = provider.GetDomainOptions(http);

        cfg.Should().NotBeNull();
        cfg.Domain.Should().Be("products");
    }

    [Fact]
    public void EnsureConfig_AppliesAllImportantDefaults()
    {
        var provider = CreateProvider(new CacheOrchestratorOptions());
        var cfg = provider.EnsureDomainOptions(new DefaultHttpContext(), "products");

        cfg.OutputCacheEnabled.Should().BeTrue();
        cfg.FusionCacheEnabled.Should().BeTrue();
        cfg.CacheableStatusCodes.Should().Contain(200);
        cfg.OutputTtl.Should().Be(TimeSpan.FromSeconds(3700));
        cfg.FusionCacheSoftTtl.Should().Be(TimeSpan.FromSeconds(3800));
        cfg.FusionCacheHardTtl.Should().Be(TimeSpan.FromSeconds(43200));
        cfg.FusionCacheFailSafe.Should().Be(TimeSpan.FromSeconds(86400));
        cfg.EncodingNormalizationList.Should().Equal("br", "gzip");
        cfg.FusionRespectAuthBypass.Should().BeTrue();
        cfg.FusionCacheRespectNoStore.Should().BeTrue();
        cfg.FusionCacheVaryOnEncoding.Should().BeTrue();
        cfg.OutputCacheVaryByHost.Should().BeTrue();
    }

    [Fact]
    public void HandBuiltDomainCacheOptions_ShareProviderBoolDefaults()
    {
        DomainCacheOptions opts = new();

        opts.OutputCacheEnabled.Should().BeTrue();
        opts.FusionCacheEnabled.Should().BeTrue();
        opts.FusionCacheRespectNoStore.Should().BeTrue();
        opts.FusionCacheVaryOnEncoding.Should().BeTrue();
        opts.FusionCacheVaryOnPublicAddress.Should().BeTrue();
        opts.OutputCacheVaryByHost.Should().BeTrue();
        opts.FusionCacheAllowBackgroundDistributed.Should().BeTrue();
        opts.FusionCacheAllowBackgroundBackplane.Should().BeTrue();
        opts.EncodingNormalizationList.Should().Equal("br", "gzip");
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("42", "42")]
    [InlineData("ABC-1", "abc-1")]
    [InlineData("!!!", "")]
    [InlineData("---", "")]
    [InlineData("default", "default")]
    [InlineData("DEFAULT", "default")]
    public void NormalizeResourceId_DoesNotCollapseGarbageToDefault(string? input, string expected)
    {
        DomainName.NormalizeResourceId(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("products", "products")]
    [InlineData("Items", "items")]
    [InlineData("!!!", "")]
    [InlineData("---", "")]
    [InlineData("default", "default")]
    public void NormalizeEntityKind_DoesNotCollapseGarbageToDefault(string? input, string expected)
    {
        DomainName.NormalizeEntityKind(input).Should().Be(expected);
    }

    // =========================
    // Config reload → snapshot invalidation (L2 global cache)
    // =========================

    [Fact]
    public void OptionsMonitorChange_ClearsGlobalSnapshotCache_AndRebuildsWithNewVersion()
    {
        var initial = new CacheOrchestratorOptions
        {
            Domains =
            {
                ["catalog"] = new()
                {
                    Version = "v1",
                    OutputCacheTtlSeconds = 60,
                    FusionCacheSoftTtlSeconds = 120
                }
            }
        };

        using DomainCacheOptionsProvider provider = CreateProvider(initial, out TestOptionsMonitor monitor);

        DomainCacheOptions before = provider.GetOrCreateDomainOptions("catalog");
        before.Version.Should().Be("v1");
        before.OutputTtl.Should().Be(TimeSpan.FromSeconds(60));
        string versionHexBefore = before.VersionHex;

        // Same process cache hit (L2)
        provider.GetOrCreateDomainOptions("catalog").Should().BeSameAs(before);

        var reloaded = new CacheOrchestratorOptions
        {
            Domains =
            {
                ["catalog"] = new()
                {
                    Version = "v2",
                    OutputCacheTtlSeconds = 90,
                    FusionCacheSoftTtlSeconds = 300
                }
            }
        };

        // Simulates IOptionsMonitor reload (appsettings change / sample playground save).
        monitor.TriggerChange(reloaded);

        DomainCacheOptions after = provider.GetOrCreateDomainOptions("catalog");

        after.Should().NotBeSameAs(before, "global snapshot cache must be cleared on options change");
        after.Version.Should().Be("v2");
        after.VersionHex.Should().NotBe(versionHexBefore);
        after.OutputTtl.Should().Be(TimeSpan.FromSeconds(90));
        after.FusionCacheSoftTtl.Should().Be(TimeSpan.FromSeconds(300));

        // Subsequent calls share the new snapshot
        provider.GetOrCreateDomainOptions("catalog").Should().BeSameAs(after);
    }

    [Fact]
    public void OptionsMonitorChange_EnsureDomainOptions_OnNewRequest_UsesReloadedSnapshot()
    {
        var initial = new CacheOrchestratorOptions
        {
            Domains = { ["orders"] = new() { Version = "gen-a" } }
        };

        using DomainCacheOptionsProvider provider = CreateProvider(initial, out TestOptionsMonitor monitor);

        DomainCacheOptions firstRequest = provider.EnsureDomainOptions(new DefaultHttpContext(), "orders");
        firstRequest.Version.Should().Be("gen-a");

        monitor.TriggerChange(new CacheOrchestratorOptions
        {
            Domains = { ["orders"] = new() { Version = "gen-b" } }
        });

        DomainCacheOptions secondRequest = provider.EnsureDomainOptions(new DefaultHttpContext(), "orders");

        secondRequest.Should().NotBeSameAs(firstRequest);
        secondRequest.Version.Should().Be("gen-b");
    }

    [Fact]
    public void OptionsMonitorChange_DoesNotReplaceSnapshotAlreadyPinnedOnHttpContext()
    {
        // L1 (HttpContext.Items) is per-request: reload mid-request must not mutate the
        // options already attached to that context (stable for the remainder of the request).
        var initial = new CacheOrchestratorOptions
        {
            Domains = { ["live"] = new() { Version = "1" } }
        };

        using DomainCacheOptionsProvider provider = CreateProvider(initial, out TestOptionsMonitor monitor);

        DefaultHttpContext http = new();
        DomainCacheOptions pinned = provider.EnsureDomainOptions(http, "live");
        pinned.Version.Should().Be("1");

        monitor.TriggerChange(new CacheOrchestratorOptions
        {
            Domains = { ["live"] = new() { Version = "2" } }
        });

        // Same request still sees L1
        DomainCacheOptions stillPinned = provider.EnsureDomainOptions(http, "live");
        stillPinned.Should().BeSameAs(pinned);
        stillPinned.Version.Should().Be("1");

        // Fresh request sees reloaded config
        DomainCacheOptions fresh = provider.EnsureDomainOptions(new DefaultHttpContext(), "live");
        fresh.Version.Should().Be("2");
        fresh.Should().NotBeSameAs(pinned);
    }

    [Fact]
    public void OptionsMonitorChange_ClearsAllDomains_NotOnlyChangedOne()
    {
        var initial = new CacheOrchestratorOptions
        {
            Domains =
            {
                ["a"] = new() { Version = "a1" },
                ["b"] = new() { Version = "b1" }
            }
        };

        using DomainCacheOptionsProvider provider = CreateProvider(initial, out TestOptionsMonitor monitor);

        DomainCacheOptions a1 = provider.GetOrCreateDomainOptions("a");
        DomainCacheOptions b1 = provider.GetOrCreateDomainOptions("b");

        monitor.TriggerChange(new CacheOrchestratorOptions
        {
            Domains =
            {
                ["a"] = new() { Version = "a2" },
                ["b"] = new() { Version = "b2" }
            }
        });

        DomainCacheOptions a2 = provider.GetOrCreateDomainOptions("a");
        DomainCacheOptions b2 = provider.GetOrCreateDomainOptions("b");

        a2.Should().NotBeSameAs(a1);
        b2.Should().NotBeSameAs(b1);
        a2.Version.Should().Be("a2");
        b2.Version.Should().Be("b2");
    }

    // =========================
    // Helpers
    // =========================

    private static DomainCacheOptionsProvider CreateProvider(CacheOrchestratorOptions options)
    {
        var monitor = new TestOptionsMonitor(options);
        return new DomainCacheOptionsProvider(monitor, NullLogger<DomainCacheOptionsProvider>.Instance);
    }

    private static DomainCacheOptionsProvider CreateProvider(
        CacheOrchestratorOptions options,
        out TestOptionsMonitor monitor)
    {
        monitor = new TestOptionsMonitor(options);
        return new DomainCacheOptionsProvider(monitor, NullLogger<DomainCacheOptionsProvider>.Instance);
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