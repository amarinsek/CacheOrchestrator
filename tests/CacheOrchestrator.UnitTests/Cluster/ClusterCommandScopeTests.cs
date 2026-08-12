using CacheOrchestrator.Cluster;

namespace CacheOrchestrator.UnitTests.Cluster;

public class ClusterCommandScopeTests
{
    [Fact]
    public void EnterRemote_SetsIsRemote_UntilDispose()
    {
        ClusterCommandScope.IsRemote.Should().BeFalse();

        using (ClusterCommandScope.EnterRemote())
        {
            ClusterCommandScope.IsRemote.Should().BeTrue();
        }

        ClusterCommandScope.IsRemote.Should().BeFalse();
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
