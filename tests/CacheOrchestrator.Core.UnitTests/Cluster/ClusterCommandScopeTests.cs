using CacheOrchestrator.Cluster;

namespace CacheOrchestrator.Core.UnitTests.Cluster;

public class ClusterCommandScopeTests
{
    [Fact]
    public void EnterRemote_SetsIsRemoteAndSuppressPublish_UntilDispose()
    {
        ClusterCommandScope.IsRemote.Should().BeFalse();
        ClusterCommandScope.SuppressPublish.Should().BeFalse();

        using (ClusterCommandScope.EnterRemote())
        {
            ClusterCommandScope.IsRemote.Should().BeTrue();
            ClusterCommandScope.SuppressPublish.Should().BeTrue();
        }

        ClusterCommandScope.IsRemote.Should().BeFalse();
        ClusterCommandScope.SuppressPublish.Should().BeFalse();
    }

    [Fact]
    public void EnterLocalOnly_SuppressesPublish_WithoutIsRemote()
    {
        using (ClusterCommandScope.EnterLocalOnly())
        {
            ClusterCommandScope.IsRemote.Should().BeFalse();
            ClusterCommandScope.SuppressPublish.Should().BeTrue();
        }

        ClusterCommandScope.SuppressPublish.Should().BeFalse();
    }

    [Fact]
    public void NestedEnterRemote_RestoresPrevious()
    {
        using (ClusterCommandScope.EnterRemote())
        {
            ClusterCommandScope.IsRemote.Should().BeTrue();
            using (ClusterCommandScope.EnterRemote())
            {
                ClusterCommandScope.IsRemote.Should().BeTrue();
            }

            ClusterCommandScope.IsRemote.Should().BeTrue();
        }

        ClusterCommandScope.IsRemote.Should().BeFalse();
    }
}
