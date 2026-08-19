using CacheOrchestrator.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.EFCore.Invalidation.UnitTests;

public class EntityCacheMappingResolverTests
{
    [Fact]
    public void Attribute_ResolvesWhenNoFluentOrMap()
    {
        using AttributeDbContext db = new();
        EntityCacheMappingResolver resolver = Create(new EfCoreInvalidationOptions());

        resolver.TryResolve(db.Model.FindEntityType(typeof(AttributedProduct))!, out EntityCacheMapping mapping)
            .Should().BeTrue();
        mapping.Domain.Should().Be("attr-store");
        mapping.EntityKind.Should().Be("attr-products");
    }

    [Fact]
    public void Map_ResolvesWhenNoAttributeOrFluent()
    {
        using MappedOnlyDbContext db = new();
        EfCoreInvalidationOptions options = new();
        options.Map<MappedOnlyRow>("store", "rows");
        EntityCacheMappingResolver resolver = Create(options);

        resolver.TryResolve(db.Model.FindEntityType(typeof(MappedOnlyRow))!, out EntityCacheMapping mapping)
            .Should().BeTrue();
        mapping.Domain.Should().Be("store");
        mapping.EntityKind.Should().Be("rows");
    }

    [Fact]
    public void Fluent_WinsOverAttribute()
    {
        using FluentDbContext db = new();
        EntityCacheMappingResolver resolver = Create(new EfCoreInvalidationOptions());

        resolver.TryResolve(db.Model.FindEntityType(typeof(AttributedProduct))!, out EntityCacheMapping mapping)
            .Should().BeTrue();
        mapping.Domain.Should().Be("fluent-store");
        mapping.EntityKind.Should().Be("fluent-products");
    }

    [Fact]
    public void Attribute_WinsOverMap()
    {
        using AttributeDbContext db = new();
        EfCoreInvalidationOptions options = new();
        options.Map<AttributedProduct>("map-store", "map-products");
        EntityCacheMappingResolver resolver = Create(options);

        resolver.TryResolve(db.Model.FindEntityType(typeof(AttributedProduct))!, out EntityCacheMapping mapping)
            .Should().BeTrue();
        mapping.Domain.Should().Be("attr-store");
        mapping.EntityKind.Should().Be("attr-products");
    }

    [Fact]
    public void UnmappedType_ReturnsFalse()
    {
        using UnmappedDbContext db = new();
        EntityCacheMappingResolver resolver = Create(new EfCoreInvalidationOptions());

        resolver.TryResolve(db.Model.FindEntityType(typeof(UnmappedRow))!, out _).Should().BeFalse();
    }

    private static EntityCacheMappingResolver Create(EfCoreInvalidationOptions options)
    {
        IOptionsMonitor<EfCoreInvalidationOptions> monitor = Substitute.For<IOptionsMonitor<EfCoreInvalidationOptions>>();
        monitor.CurrentValue.Returns(options);
        return new EntityCacheMappingResolver(monitor);
    }

    [CacheEntity("attr-store", "attr-products")]
    public sealed class AttributedProduct
    {
        public int Id { get; set; }
    }

    public sealed class MappedOnlyRow
    {
        public int Id { get; set; }
    }

    public sealed class UnmappedRow
    {
        public int Id { get; set; }
    }

    private sealed class AttributeDbContext : DbContext
    {
        public AttributeDbContext()
            : base(new DbContextOptionsBuilder<AttributeDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options)
        {
        }

        public DbSet<AttributedProduct> Products => Set<AttributedProduct>();
    }

    private sealed class MappedOnlyDbContext : DbContext
    {
        public MappedOnlyDbContext()
            : base(new DbContextOptionsBuilder<MappedOnlyDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options)
        {
        }

        public DbSet<MappedOnlyRow> Rows => Set<MappedOnlyRow>();
    }

    private sealed class FluentDbContext : DbContext
    {
        public FluentDbContext()
            : base(new DbContextOptionsBuilder<FluentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options)
        {
        }

        public DbSet<AttributedProduct> Products => Set<AttributedProduct>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AttributedProduct>().CacheInvalidate("fluent-store", "fluent-products");
        }
    }

    private sealed class UnmappedDbContext : DbContext
    {
        public UnmappedDbContext()
            : base(new DbContextOptionsBuilder<UnmappedDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options)
        {
        }

        public DbSet<UnmappedRow> Rows => Set<UnmappedRow>();
    }
}
