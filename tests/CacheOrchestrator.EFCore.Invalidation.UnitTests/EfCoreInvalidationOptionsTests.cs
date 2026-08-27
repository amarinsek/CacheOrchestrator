using CacheOrchestrator.EFCore;

namespace CacheOrchestrator.EFCore.Invalidation.UnitTests;

public class EfCoreInvalidationOptionsTests
{
    [Fact]
    public void Defaults_AreEnabledKindThreshold20()
    {
        EfCoreInvalidationOptions options = new();
        options.Enabled.Should().BeTrue();
        options.BulkThreshold.Should().Be(20);
        options.OnBulk.Should().Be(EfCoreOnBulk.Kind);
    }

    [Fact]
    public void Map_StoresTypeAndTrims()
    {
        EfCoreInvalidationOptions options = new();
        options.Map<MappedRow>("  store  ", "  rows  ").Should().BeSameAs(options);

        options.TryGetTypeMap(typeof(MappedRow), out EntityCacheMapping mapping).Should().BeTrue();
        mapping.Domain.Should().Be("store");
        mapping.EntityKind.Should().Be("rows");
    }

    [Theory]
    [InlineData(null, "rows")]
    [InlineData(" ", "rows")]
    [InlineData("store", null)]
    [InlineData("store", "  ")]
    public void Map_WhenDomainOrKindMissing_Throws(string? domain, string? kind)
    {
        EfCoreInvalidationOptions options = new();
        Func<EfCoreInvalidationOptions> act = () => options.Map<MappedRow>(domain!, kind!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryGetTypeMap_WhenUnmapped_ReturnsFalse()
    {
        EfCoreInvalidationOptions options = new();
        options.TryGetTypeMap(typeof(MappedRow), out _).Should().BeFalse();
    }

    public sealed class MappedRow
    {
        public int Id { get; set; }
    }
}
