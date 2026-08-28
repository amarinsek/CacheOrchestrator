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
}
