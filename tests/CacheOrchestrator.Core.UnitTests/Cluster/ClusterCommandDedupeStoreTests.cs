using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Core.UnitTests.Cluster;

public class ClusterCommandDedupeStoreTests
{
    [Fact]
    public void TryMarkAsNew_SecondCallWithinWindow_ReturnsFalse()
    {
        IOptionsMonitor<CacheOrchestratorOptions> options = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            Cluster = { Bus = { DedupeWindowSeconds = 120 } }
        });

        ClusterCommandDedupeStore store = new(options);
        Guid id = Guid.NewGuid();

        store.TryMarkAsNew(id).Should().BeTrue();
        store.TryMarkAsNew(id).Should().BeFalse();
    }

    [Fact]
    public void TryMarkAsNew_WhenWindowDisabled_AlwaysTrue()
    {
        IOptionsMonitor<CacheOrchestratorOptions> options = Substitute.For<IOptionsMonitor<CacheOrchestratorOptions>>();
        options.CurrentValue.Returns(new CacheOrchestratorOptions
        {
            Cluster = { Bus = { DedupeWindowSeconds = 0 } }
        });

        ClusterCommandDedupeStore store = new(options);
        Guid id = Guid.NewGuid();

        store.TryMarkAsNew(id).Should().BeTrue();
        store.TryMarkAsNew(id).Should().BeTrue();
    }
}
