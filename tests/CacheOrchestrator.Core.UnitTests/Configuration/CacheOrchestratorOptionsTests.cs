using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;

namespace CacheOrchestrator.Core.UnitTests.Configuration;

public class CacheOrchestratorOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var opts = new CacheOrchestratorOptions();

        opts.Namespace.Should().Be("app-cache");
        opts.DataCacheInstances["default"].Provider.Should().Be("InMemory");
        opts.Domains.Should().BeEmpty();
        opts.Distributed.SoftTimeoutSeconds.Should().Be(1);
        opts.Distributed.HardTimeoutSeconds.Should().Be(2);
        opts.Distributed.CircuitBreakerSeconds.Should().Be(5);
    }

    [Fact]
    public void GetNamespace_WhenInstanceNamespaceNull_UsesGlobalSuffix()
    {
        var opts = new CacheOrchestratorOptions();
        opts.DataCacheInstances["pii"] = new CacheOrchestratorOptions.DataCacheInstanceOptions { Namespace = null };
        opts.Namespace = "myapp";

        opts.DataCacheInstances["pii"].GetNamespace("pii", opts).Should().Be("myapp-fc-pii");
    }

    [Fact]
    public void GetNamespace_WhenDefaultInstance_OmitsDefaultSuffix()
    {
        var opts = new CacheOrchestratorOptions { Namespace = "myapp" };
        opts.DataCacheInstances["default"] = new CacheOrchestratorOptions.DataCacheInstanceOptions { Namespace = null };

        opts.DataCacheInstances["default"].GetNamespace("default", opts).Should().Be("myapp-fc");
        opts.DataCacheInstances["default"].GetNamespace("Default", opts).Should().Be("myapp-fc");
    }

    [Fact]
    public void GetNamespace_WhenInstanceNamespaceSet_UsesInstanceNamespace()
    {
        var opts = new CacheOrchestratorOptions();
        opts.DataCacheInstances["pii"] = new CacheOrchestratorOptions.DataCacheInstanceOptions { Namespace = "custom-fc" };
        opts.Namespace = "myapp";

        opts.DataCacheInstances["pii"].GetNamespace("pii", opts).Should().Be("custom-fc");
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
