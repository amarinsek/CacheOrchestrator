using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Core.UnitTests.Configuration;

public class CacheTagsTests
{
    [Fact]
    public void Entity_EncodesEachSegmentIndependently()
    {
        string first = CacheTags.Entity("a:b", "c", "d/e");
        string second = CacheTags.Entity("a", "b:c", "d/e");

        first.Should().Be("entity:a%3Ab:c:d%2Fe");
        second.Should().Be("entity:a:b%3Ac:d%2Fe");
        first.Should().NotBe(second);
    }

    [Fact]
    public void Entity_PreservesOpaqueResourceIdentity()
    {
        CacheTags.Entity("store", "products", "A/B")
            .Should().NotBe(CacheTags.Entity("store", "products", "A B"));
    }
}
