using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AspNetCore.UnitTests.Configuration;

public class MultiInstanceOptionsTests
{
    private static IRequestDomainCacheOptions BuildProvider(CacheOrchestratorOptions opts)
    {
        IOptionsMonitor<CacheOrchestratorOptions> monitor = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        monitor.CurrentValue.Returns(opts);
        monitor.OnChange(Arg.Any<Action<CacheOrchestratorOptions, string?>>()).Returns((IDisposable?)null);
        IOptionsMonitor<CacheOrchestratorHttpOptions> httpMonitor =
            new FixedOptionsMonitor<CacheOrchestratorHttpOptions>(new CacheOrchestratorHttpOptions());
        DomainCacheOptionsProvider inner = new(monitor, NullLogger<DomainCacheOptionsProvider>.Instance);
        return new RequestDomainCacheOptionsProvider(
            inner,
            monitor,
            httpMonitor,
            NullLogger<RequestDomainCacheOptionsProvider>.Instance,
            new CacheOrchestrator.Admin.DomainRuntimeOverrideStore(),
            new HttpDomainRuntimeOverrideStore());
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static CacheOrchestratorOptions TwoInstanceOptions() => new()
    {
        Namespace = "my-app",
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
        IRequestDomainCacheOptions provider = BuildProvider(TwoInstanceOptions());

        DomainHttpCacheOptions opts = provider.GetOrCreateDomainOptions("users");

        opts.DataCacheInstanceName.Should().Be("pii");
    }

    [Fact]
    public void GetOrCreateDomainOptions_DomainWithDefaultInstance_ResolvesDefault()
    {
        IRequestDomainCacheOptions provider = BuildProvider(TwoInstanceOptions());

        DomainHttpCacheOptions opts = provider.GetOrCreateDomainOptions("products");

        opts.DataCacheInstanceName.Should().Be("default");
    }

    [Fact]
    public void GetOrCreateDomainOptions_UnknownDomain_FallsBackToDefault()
    {
        IRequestDomainCacheOptions provider = BuildProvider(TwoInstanceOptions());

        DomainHttpCacheOptions opts = provider.GetOrCreateDomainOptions("news");

        opts.DataCacheInstanceName.Should().Be("default");
    }

    [Fact]
    public void GetOrCreateDomainOptions_DomainWithNullInstance_FallsBackToDefault()
    {
        CacheOrchestratorOptions options = TwoInstanceOptions();
        options.Domains["reports"] = new CacheOrchestratorOptions.DomainCacheSettings
        {
            DataCache = new() { Instance = null }
        };
        IRequestDomainCacheOptions provider = BuildProvider(options);

        DomainHttpCacheOptions opts = provider.GetOrCreateDomainOptions("reports");

        opts.DataCacheInstanceName.Should().Be("default");
    }

    [Fact]
    public void GetOrCreateDomainOptions_DomainDefaultsFusionInstance_PropagatesDown()
    {
        CacheOrchestratorOptions options = TwoInstanceOptions();
        options.DomainDefaults.DataCache = new() { Instance = "pii" };
        // "products" has explicit "default", "users" has "pii", "news" has no override
        IRequestDomainCacheOptions provider = BuildProvider(options);

        // "news" has no entry â†’ inherits DomainDefaults â†’ "pii"
        DomainHttpCacheOptions opts = provider.GetOrCreateDomainOptions("news");

        opts.DataCacheInstanceName.Should().Be("pii");
    }

    [Fact]
    public void GetOrCreateDomainOptions_ExplicitInstanceOverridesDomainDefaults()
    {
        CacheOrchestratorOptions options = TwoInstanceOptions();
        options.DomainDefaults.DataCache = new() { Instance = "pii" };
        // "products" explicitly overrides to "default"
        IRequestDomainCacheOptions provider = BuildProvider(options);

        DomainHttpCacheOptions opts = provider.GetOrCreateDomainOptions("products");

        opts.DataCacheInstanceName.Should().Be("default");
    }

    // =========================
    // Namespace per instance
    // =========================

    [Fact]
    public void GetOrCreateDomainOptions_PiiInstance_UsesInstanceNamespace()
    {
        IRequestDomainCacheOptions provider = BuildProvider(TwoInstanceOptions());

        DomainHttpCacheOptions opts = provider.GetOrCreateDomainOptions("users");

        opts.DataCacheNamespace.Should().Be("my-app-pii");
    }

    [Fact]
    public void GetOrCreateDomainOptions_DefaultInstance_UsesGeneratedNamespace()
    {
        IRequestDomainCacheOptions provider = BuildProvider(TwoInstanceOptions());

        DomainHttpCacheOptions opts = provider.GetOrCreateDomainOptions("products");

        // default has no explicit Namespace → falls back to "{Namespace}-dc" (no -default suffix)
        opts.DataCacheNamespace.Should().Be("my-app-dc");
    }

    // =========================
    // EnsureDomainOptions + HttpContext (via GetOrCreate path)
    // =========================

    [Fact]
    public void EnsureDomainOptions_SetsCorrectInstanceNameOnHttp()
    {
        IRequestDomainCacheOptions provider = BuildProvider(TwoInstanceOptions());
        var http = new DefaultHttpContext();

        DomainHttpCacheOptions opts = provider.EnsureDomainOptions(http, "users");

        opts.DataCacheInstanceName.Should().Be("pii");
    }

    // =========================
    // GetOrCreateDomainOptions without HttpContext
    // =========================

    [Fact]
    public void GetOrCreateDomainOptions_CalledTwice_ReturnsSameInstance()
    {
        IRequestDomainCacheOptions provider = BuildProvider(TwoInstanceOptions());

        DomainHttpCacheOptions first = provider.GetOrCreateDomainOptions("products");
        DomainHttpCacheOptions second = provider.GetOrCreateDomainOptions("products");

        first.Should().BeSameAs(second);
    }
}
