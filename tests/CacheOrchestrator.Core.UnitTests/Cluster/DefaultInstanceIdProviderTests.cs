using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Core.UnitTests.Cluster;

public class DefaultInstanceIdProviderTests
{
    [Fact]
    public void Resolve_WhenConfigured_UsesTrimmedValue()
    {
        DefaultInstanceIdProvider.Resolve("  node-a  ").Should().Be("node-a");
    }

    [Fact]
    public void Resolve_WhenEmpty_UsesMachineName()
    {
        string id = DefaultInstanceIdProvider.Resolve(null);
        id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Provider_ReadsRootInstanceId()
    {
        IOptions<CacheOrchestratorOptions> options = Options.Create(new CacheOrchestratorOptions
        {
            InstanceId = "from-root"
        });

        DefaultInstanceIdProvider provider = new(options);
        provider.InstanceId.Should().Be("from-root");
    }
}
