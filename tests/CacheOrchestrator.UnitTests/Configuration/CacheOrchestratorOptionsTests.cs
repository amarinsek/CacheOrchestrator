using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;

namespace CacheOrchestrator.UnitTests.Configuration;

public class CacheOrchestratorOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var opts = new CacheOrchestratorOptions();

        opts.Namespace.Should().Be("app-cache");
        opts.EmitDiagnosticsHeaders.Should().BeTrue();
        opts.OutputCache.Provider.Should().Be("InMemory");
        opts.FusionCacheInstances["default"].Provider.Should().Be("InMemory");
        opts.Domains.Should().BeEmpty();
        opts.Distributed.SoftTimeoutSeconds.Should().Be(1);
        opts.Distributed.HardTimeoutSeconds.Should().Be(2);
        opts.Distributed.CircuitBreakerSeconds.Should().Be(5);
    }

    [Fact]
    public void EmitDiagnosticsHeaders_BindsFromConfiguration()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:EmitDiagnosticsHeaders"] = "false"
            })
            .Build();

        var opts = new CacheOrchestratorOptions();
        config.GetSection("Cache").Bind(opts);

        opts.EmitDiagnosticsHeaders.Should().BeFalse();
    }

    [Fact]
    public void OutputNamespace_WhenProviderNamespaceNull_UsesGlobalSuffix()
    {
        var opts = new CacheOrchestratorOptions { Namespace = "myapp" };
        opts.OutputCache.Namespace = null;

        opts.OutputNamespace.Should().Be("myapp-oc");
    }

    [Fact]
    public void GetNamespace_WhenInstanceNamespaceNull_UsesGlobalSuffix()
    {
        var opts = new CacheOrchestratorOptions();
        opts.FusionCacheInstances["pii"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Namespace = null };
        opts.Namespace = "myapp";

        opts.FusionCacheInstances["pii"].GetNamespace("pii", opts).Should().Be("myapp-fc-pii");
    }

    [Fact]
    public void GetNamespace_WhenDefaultInstance_OmitsDefaultSuffix()
    {
        var opts = new CacheOrchestratorOptions { Namespace = "myapp" };
        opts.FusionCacheInstances["default"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Namespace = null };

        opts.FusionCacheInstances["default"].GetNamespace("default", opts).Should().Be("myapp-fc");
        opts.FusionCacheInstances["default"].GetNamespace("Default", opts).Should().Be("myapp-fc");
    }

    [Fact]
    public void OutputNamespace_WhenProviderNamespaceSet_UsesProviderNamespace()
    {
        var opts = new CacheOrchestratorOptions { Namespace = "myapp" };
        opts.OutputCache.Namespace = "custom-oc";

        opts.OutputNamespace.Should().Be("custom-oc");
    }

    [Fact]
    public void GetNamespace_WhenInstanceNamespaceSet_UsesInstanceNamespace()
    {
        var opts = new CacheOrchestratorOptions();
        opts.FusionCacheInstances["pii"] = new CacheOrchestratorOptions.FusionCacheInstanceOptions { Namespace = "custom-fc" };
        opts.Namespace = "myapp";

        opts.FusionCacheInstances["pii"].GetNamespace("pii", opts).Should().Be("custom-fc");
    }

    [Fact]
    public void Domains_IsCaseInsensitive()
    {
        var opts = new CacheOrchestratorOptions();
        opts.Domains["Products"] = new CacheOrchestratorOptions.DomainCacheSettings();

        opts.Domains.ContainsKey("products").Should().BeTrue();
        opts.Domains.ContainsKey("PRODUCTS").Should().BeTrue();
    }

    [Fact]
    public void GetEffectiveDistributedResilience_ReturnsDistributed()
    {
        var opts = new CacheOrchestratorOptions
        {
            Distributed = { SoftTimeoutSeconds = 3, HardTimeoutSeconds = 7, CircuitBreakerSeconds = 12 }
        };

        DistributedResilienceOptions r = opts.GetEffectiveDistributedResilience();
        r.SoftTimeoutSeconds.Should().Be(3);
        r.HardTimeoutSeconds.Should().Be(7);
        r.CircuitBreakerSeconds.Should().Be(12);
    }
}
