using CacheOrchestrator.Backends;
using Microsoft.Extensions.Configuration;

namespace CacheOrchestrator.AspNetCore.UnitTests.Backends;

public class BackendConfigurationTests
{
    [Fact]
    public void GetOutputBackendSection_UsesStandardPath()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:OutputCache:SqlServer:ConnectionString"] = "Server=.;Database=x"
            })
            .Build();

        IConfigurationSection section = BackendConfiguration.GetOutputBackendSection(config, "Cache", "SqlServer");
        section.Exists().Should().BeTrue();
        section["ConnectionString"].Should().Be("Server=.;Database=x");
    }

    [Fact]
    public void GetFusionBackendSection_UsesStandardPath()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:DataCacheInstances:default:SqlServer:ConnectionString"] = "Server=.;Database=fc"
            })
            .Build();

        IConfigurationSection section =
            BackendConfiguration.GetFusionBackendSection(config, "Cache", "default", "SqlServer");
        section["ConnectionString"].Should().Be("Server=.;Database=fc");
    }
}
