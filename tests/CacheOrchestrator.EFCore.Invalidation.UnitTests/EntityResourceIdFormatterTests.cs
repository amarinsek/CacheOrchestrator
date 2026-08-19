using CacheOrchestrator.Configuration;
using CacheOrchestrator.EFCore;
using Microsoft.EntityFrameworkCore;

namespace CacheOrchestrator.EFCore.Invalidation.UnitTests;

public class EntityResourceIdFormatterTests
{
    [Fact]
    public void GuidPk_MatchesNormalizedRouteValue()
    {
        Guid id = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
        using GuidDbContext db = Create(id);

        string? formatted = EntityResourceIdFormatter.TryFormat(db.Entry(db.Rows.Single()));
        string fromRoute = DomainName.NormalizeResourceId(id.ToString());
        string fromUpperRoute = DomainName.NormalizeResourceId("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

        formatted.Should().Be(fromRoute);
        formatted.Should().Be(fromUpperRoute);
        formatted.Should().Be("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    }

    [Fact]
    public void ByteArrayPk_FormatsAsLowerHex()
    {
        byte[] id = [0xAB, 0xCD];
        using ByteDbContext db = CreateBytes(id);

        string? formatted = EntityResourceIdFormatter.TryFormat(db.Entry(db.Rows.Single()));
        formatted.Should().Be("abcd");
    }

    [Fact]
    public void TryFormat_WhenEntryIsNull_Throws()
    {
        var act = () => EntityResourceIdFormatter.TryFormat(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static ByteDbContext CreateBytes(byte[] id)
    {
        DbContextOptions<ByteDbContext> options = new DbContextOptionsBuilder<ByteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        ByteDbContext db = new(options);
        db.Rows.Add(new ByteRow { Id = id });
        db.SaveChanges();
        return db;
    }

    public sealed class ByteRow
    {
        public byte[] Id { get; set; } = [];
    }

    private sealed class ByteDbContext : DbContext
    {
        public ByteDbContext(DbContextOptions<ByteDbContext> options)
            : base(options)
        {
        }

        public DbSet<ByteRow> Rows => Set<ByteRow>();
    }

    private static GuidDbContext Create(Guid id)
    {
        DbContextOptions<GuidDbContext> options = new DbContextOptionsBuilder<GuidDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        GuidDbContext db = new(options);
        db.Rows.Add(new GuidRow { Id = id });
        db.SaveChanges();
        return db;
    }

    public sealed class GuidRow
    {
        public Guid Id { get; set; }
    }

    private sealed class GuidDbContext : DbContext
    {
        public GuidDbContext(DbContextOptions<GuidDbContext> options)
            : base(options)
        {
        }

        public DbSet<GuidRow> Rows => Set<GuidRow>();
    }
}
