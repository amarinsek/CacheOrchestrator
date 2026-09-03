using CacheOrchestrator.Edge.Tags;

namespace CacheOrchestrator.Edge.UnitTests;

public class EdgeTagProjectorTests
{
    [Fact]
    public void Project_IsStableOpaqueAndNamespaceScoped()
    {
        var sut = new EdgeTagProjector();

        string first = sut.Project("shop", "entity:catalog:products:Customer-42");
        string again = sut.Project("shop", "entity:catalog:products:Customer-42");
        string otherNamespace = sut.Project("other", "entity:catalog:products:Customer-42");

        first.Should().Be(again);
        first.Should().StartWith("coe1-");
        first.Should().NotContain("Customer-42");
        first.Should().NotBe(otherNamespace);
        first.Should().MatchRegex("^[a-z0-9-]+$");
    }
}
