using CacheOrchestrator.Cluster;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Core.UnitTests.Cluster;

public class ClusterCommandDedupeStoreTests
{
    [Fact]
    public void TryMarkAsNew_SecondCallWithinWindow_ReturnsFalse()
    {
        IOptionsMonitor<ClusterCommandHandlingOptions> options =
            new FixedOptionsMonitor<ClusterCommandHandlingOptions>(
                new ClusterCommandHandlingOptions { DedupeWindowSeconds = 120 });

        ClusterCommandDedupeStore store = new(options);
        var id = Guid.NewGuid();

        store.TryMarkAsNew(id).Should().BeTrue();
        store.TryMarkAsNew(id).Should().BeFalse();
    }

    [Fact]
    public void TryMarkAsNew_WhenWindowDisabled_AlwaysTrue()
    {
        IOptionsMonitor<ClusterCommandHandlingOptions> options =
            new FixedOptionsMonitor<ClusterCommandHandlingOptions>(
                new ClusterCommandHandlingOptions { DedupeWindowSeconds = 0 });

        ClusterCommandDedupeStore store = new(options);
        var id = Guid.NewGuid();

        store.TryMarkAsNew(id).Should().BeTrue();
        store.TryMarkAsNew(id).Should().BeTrue();
    }
}
