using CacheOrchestrator.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CacheOrchestrator.EFCore.Invalidation.UnitTests;

public class EntityTypeBuilderExtensionsTests
{
    [Fact]
    public void CacheInvalidate_SetsAnnotations()
    {
        ModelBuilder modelBuilder = new();
        modelBuilder.Entity<Widget>().CacheInvalidate("store", "widgets");
        IMutableEntityType entity = modelBuilder.Model.FindEntityType(typeof(Widget))!;

        entity.FindAnnotation(CacheOrchestratorEfAnnotations.Domain)!.Value.Should().Be("store");
        entity.FindAnnotation(CacheOrchestratorEfAnnotations.EntityKind)!.Value.Should().Be("widgets");
    }

    [Fact]
    public void CacheInvalidate_WhenDomainIsWhitespace_Throws()
    {
        ModelBuilder modelBuilder = new();
        Func<EntityTypeBuilder<Widget>> act = () => modelBuilder.Entity<Widget>().CacheInvalidate("  ", "widgets");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CacheInvalidate_NonGeneric_SetsAnnotations()
    {
        ModelBuilder modelBuilder = new();
        modelBuilder.Entity(typeof(Widget)).CacheInvalidate("store", "widgets");
        IMutableEntityType entity = modelBuilder.Model.FindEntityType(typeof(Widget))!;

        entity.FindAnnotation(CacheOrchestratorEfAnnotations.Domain)!.Value.Should().Be("store");
        entity.FindAnnotation(CacheOrchestratorEfAnnotations.EntityKind)!.Value.Should().Be("widgets");
    }

    public sealed class Widget
    {
        public int Id { get; set; }
    }
}
