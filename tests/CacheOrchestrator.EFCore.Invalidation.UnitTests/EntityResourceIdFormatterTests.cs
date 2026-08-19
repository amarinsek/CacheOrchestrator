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
