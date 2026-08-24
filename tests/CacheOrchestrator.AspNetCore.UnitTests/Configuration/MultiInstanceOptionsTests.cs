using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AspNetCore.UnitTests.Configuration;

public class MultiInstanceOptionsTests
{
    private static IRequestDomainCacheOptions BuildProvider(CacheOrchestratorOptions opts)
    {
        var monitor = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        monitor.CurrentValue.Returns(opts);
        monitor.OnChange(Arg.Any<Action<CacheOrchestratorOptions, string?>>()).Returns((IDisposable?)null);
        DomainCacheOptionsProvider inner = new(monitor, NullLogger<DomainCacheOptionsProvider>.Instance);
        return new RequestDomainCacheOptionsProvider(inner, NullLogger<RequestDomainCacheOptionsProvider>.Instance);
    }

    private static CacheOrchestratorOptions TwoInstanceOptions() => new()
    {
        Namespace = "my-app",
        OutputCache = { Provider = "InMemory" },
        DataCacheInstances = new Dictionary<string, CacheOrchestratorOptions.DataCacheInstanceOptions>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = new() { Provider = "InMemory" },
            ["pii"] = new() { Provider = "InMemory", Namespace = "my-app-pii" }
        },
        Domains = new Dictionary<string, CacheOrchestratorOptions.DomainCacheSettings>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["products"] = new() { DataCache = new() { Instance = "default" } },
            ["users"] = new() { DataCache = new() { Instance = "pii" } }
        }
    };

    // =========================
    // Instance name resolution
    // =========================

    [Fact]
    public void GetOrCreateDomainOptions_DomainWithExplicitInstance_ResolvesCorrectInstance()
    {
        var provider = BuildProvider(TwoInstanceOptions());

        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("users");

        opts.DataCacheInstanceName.Should().Be("pii");
    }

    [Fact]
    public void GetOrCreateDomainOptions_DomainWithDefaultInstance_ResolvesDefault()
    {
        var provider = BuildProvider(TwoInstanceOptions());

        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("products");

        opts.DataCacheInstanceName.Should().Be("default");
    }

    [Fact]
    public void GetOrCreateDomainOptions_UnknownDomain_FallsBackToDefault()
    {
        var provider = BuildProvider(TwoInstanceOptions());

        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("news");

        opts.DataCacheInstanceName.Should().Be("default");
    }

    [Fact]
    public void GetOrCreateDomainOptions_DomainWithNullInstance_FallsBackToDefault()
    {
        var options = TwoInstanceOptions();
        options.Domains["reports"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            DataCache = new() { Instance = null }
        };
        var provider = BuildProvider(options);

        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("reports");

        opts.DataCacheInstanceName.Should().Be("default");
    }

    [Fact]
    public void GetOrCreateDomainOptions_DomainDefaultsFusionInstance_PropagatesDown()
    {
        var options = TwoInstanceOptions();
        options.DomainDefaults.DataCache = new() { Instance = "pii" };
        // "products" has explicit "default", "users" has "pii", "news" has no override
        var provider = BuildProvider(options);

        // "news" has no entry â†’ inherits DomainDefaults â†’ "pii"
        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("news");

        opts.DataCacheInstanceName.Should().Be("pii");
    }

    [Fact]
    public void GetOrCreateDomainOptions_ExplicitInstanceOverridesDomainDefaults()
    {
        var options = TwoInstanceOptions();
        options.DomainDefaults.DataCache = new() { Instance = "pii" };
        // "products" explicitly overrides to "default"
        var provider = BuildProvider(options);

        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("products");

        opts.DataCacheInstanceName.Should().Be("default");
    }

    // =========================
    // Namespace per instance
    // =========================

    [Fact]
    public void GetOrCreateDomainOptions_PiiInstance_UsesInstanceNamespace()
    {
        var provider = BuildProvider(TwoInstanceOptions());

        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("users");

        opts.DataCacheNamespace.Should().Be("my-app-pii");
    }

    [Fact]
    public void GetOrCreateDomainOptions_DefaultInstance_UsesGeneratedNamespace()
    {
        var provider = BuildProvider(TwoInstanceOptions());

        DomainCacheOptions opts = provider.GetOrCreateDomainOptions("products");

        // default has no explicit Namespace â†’ falls back to "{Namespace}-fc" (no -default suffix)
        opts.DataCacheNamespace.Should().Be("my-app-fc");
    }

    // =========================
    // EnsureDomainOptions + HttpContext (via GetOrCreate path)
    // =========================

    [Fact]
    public void EnsureDomainOptions_SetsCorrectInstanceNameOnHttp()
    {
        var provider = BuildProvider(TwoInstanceOptions());
        var http = new DefaultHttpContext();

        DomainCacheOptions opts = provider.EnsureDomainOptions(http, "users");

        opts.DataCacheInstanceName.Should().Be("pii");
    }

    // =========================
    // GetOrCreateDomainOptions without HttpContext
    // =========================

    [Fact]
    public void GetOrCreateDomainOptions_CalledTwice_ReturnsSameInstance()
    {
        var provider = BuildProvider(TwoInstanceOptions());

        DomainCacheOptions first = provider.GetOrCreateDomainOptions("products");
        DomainCacheOptions second = provider.GetOrCreateDomainOptions("products");

        first.Should().BeSameAs(second);
    }
}