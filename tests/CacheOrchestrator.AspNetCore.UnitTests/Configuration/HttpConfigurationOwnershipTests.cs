using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.AspNetCore.UnitTests.Configuration;

public class HttpConfigurationOwnershipTests
{
    [Fact]
    public void SameConfigurationSection_BindsPortableAndHttpDataCacheSettingsToTheirOwners()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Domains:products:DataCache:Enabled"] = "true",
                ["Cache:Domains:products:DataCache:TtlSeconds"] = "42",
                ["Cache:Domains:products:DataCache:RespectNoStore"] = "false",
                ["Cache:Domains:products:DataCache:VaryOnPublicAddress"] = "false",
                ["Cache:Domains:products:DataCache:VaryOnEncoding"] = "true",
                ["Cache:Domains:products:OutputCache:TtlSeconds"] = "30",
                ["Cache:Domains:products:ClientCache:TtlSeconds"] = "15"
            })
            .Build();

        CacheOrchestratorOptions core = new();
        CacheOrchestratorHttpOptions http = new();
        configuration.GetSection("Cache").Bind(core);
        configuration.GetSection("Cache").Bind(http);

        core.Domains["products"].DataCache!.Enabled.Should().BeTrue();
        core.Domains["products"].DataCache!.TtlSeconds.Should().Be(42);
        http.Domains["products"].DataCache!.RespectNoStore.Should().BeFalse();
        http.Domains["products"].DataCache!.VaryOnPublicAddress.Should().BeFalse();
        http.Domains["products"].DataCache!.VaryOnEncoding.Should().BeTrue();
        http.Domains["products"].OutputCache!.TtlSeconds.Should().Be(30);
        http.Domains["products"].ClientCache!.TtlSeconds.Should().Be(15);
    }

    [Fact]
    public void HttpRootSettings_BindToAspNetCoreOptions()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Namespace"] = "shop",
                ["Cache:EmitDiagnosticsHeaders"] = "false",
                ["Cache:Metrics:IncludeEndpointLabel"] = "false",
                ["Cache:OutputCache:Provider"] = "Redis",
                ["Cache:OutputCache:Namespace"] = "shop-output",
                ["Cache:Admin:Enabled"] = "true",
                ["Cache:Admin:ApiKey"] = "secret",
                ["Cache:Admin:RoutePrefix"] = "/management/cache"
            })
            .Build();

        CacheOrchestratorHttpOptions options = new();
        configuration.GetSection("Cache").Bind(options);

        options.EmitDiagnosticsHeaders.Should().BeFalse();
        options.Metrics.IncludeEndpointLabel.Should().BeFalse();
        options.OutputCache.Provider.Should().Be("Redis");
        options.OutputNamespace.Should().Be("shop-output");
        options.Admin.Enabled.Should().BeTrue();
        options.Admin.ApiKey.Should().Be("secret");
        options.Admin.RoutePrefix.Should().Be("/management/cache");
    }

    [Fact]
    public void HttpValidator_RejectsHttpTtlAndVaryErrors()
    {
        CacheOrchestratorHttpOptions options = new()
        {
            Domains =
            {
                ["products"] = new()
                {
                    OutputCache = new() { TtlSeconds = -1 },
                    VaryByHeaders = ["Accept", " "]
                }
            }
        };

        ValidateOptionsResult result = new CacheOrchestratorHttpOptionsValidator().Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(message => message.Contains("OutputCache.TtlSeconds", StringComparison.Ordinal));
        result.Failures.Should().Contain(message => message.Contains("VaryByHeaders[1]", StringComparison.Ordinal));
    }

    [Fact]
    public void HttpValidator_RejectsOutputProviderNotRegisteredByAspNetCoreHost()
    {
        CacheOrchestratorHttpOptions options = new()
        {
            OutputCache = { Provider = "Redis" }
        };

        ValidateOptionsResult result = new CacheOrchestratorHttpOptionsValidator(["InMemory"])
            .Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(message => message.Contains("Redis", StringComparison.Ordinal));
    }

    [Fact]
    public void HttpValidator_RejectsInheritedClientTtlRelationship()
    {
        CacheOrchestratorHttpOptions options = new()
        {
            DomainDefaults = new()
            {
                ClientCache = new() { TtlSeconds = 30, TtlMinSeconds = 10 }
            },
            Domains =
            {
                ["products"] = new()
                {
                    ClientCache = new() { TtlMinSeconds = 40 }
                }
            }
        };

        ValidateOptionsResult result = new CacheOrchestratorHttpOptionsValidator().Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(message =>
            message.Contains("ClientCache.TtlMinSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void HttpValidator_AllowsZeroClientTtl_WithInheritedMinimum()
    {
        CacheOrchestratorHttpOptions options = new()
        {
            DomainDefaults = new()
            {
                ClientCache = new() { TtlMinSeconds = 60 }
            },
            Domains =
            {
                ["products"] = new()
                {
                    ClientCache = new() { TtlSeconds = 0 }
                }
            }
        };

        ValidateOptionsResult result = new CacheOrchestratorHttpOptionsValidator().Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }
}
