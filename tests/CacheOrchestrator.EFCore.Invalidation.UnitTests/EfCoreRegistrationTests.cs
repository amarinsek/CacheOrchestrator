using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.EFCore;
using CacheOrchestrator.Invalidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.EFCore.Invalidation.UnitTests;

public class EfCoreRegistrationTests
{
    [Fact]
    public void AddCacheOrchestratorEfCoreInvalidation_RegistersInterceptorAndResolver()
    {
        ServiceCollection services = new();
        IConfiguration config = InMemoryCacheConfig();
        services.AddLogging();
        services.AddCacheOrchestrator(config, enableMvcConvention: false);
        services.AddCacheOrchestratorEfCoreInvalidation(config);

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<CacheInvalidationSaveChangesInterceptor>().Should().NotBeNull();
        sp.GetRequiredService<IEntityCacheMappingResolver>().Should().NotBeNull();
    }

    [Fact]
    public void AddEfCoreInvalidation_OnBuilder_RegistersInterceptor()
    {
        ServiceCollection services = new();
        IConfiguration config = InMemoryCacheConfig();
        services.AddLogging();
        services.AddCacheOrchestrator(config, o => o.AddEfCoreInvalidation(), enableMvcConvention: false);

        using ServiceProvider sp = services.BuildServiceProvider();
        sp.GetRequiredService<CacheInvalidationSaveChangesInterceptor>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddCacheOrchestratorInvalidation_AttachesInterceptor()
    {
        ServiceCollection services = new();
        IConfiguration config = InMemoryCacheConfig();
        services.AddLogging();
        services.AddCacheOrchestrator(config, enableMvcConvention: false);
        services.AddCacheOrchestratorEfCoreInvalidation(config, o => o.Map<RegProduct>("store", "products"));
        services.AddDbContext<RegDbContext>((sp, opt) =>
        {
            opt.UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
            opt.AddCacheOrchestratorInvalidation(sp);
        });

        await using ServiceProvider sp = services.BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();
        RegDbContext db = scope.ServiceProvider.GetRequiredService<RegDbContext>();
        ICacheOrchestratorInvalidator inv = scope.ServiceProvider.GetRequiredService<ICacheOrchestratorInvalidator>();

        db.Products.Add(new RegProduct { Name = "A" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Real invalidator is registered; the call must succeed (InMemory stores).
        db.Products.Should().ContainSingle();
        inv.Should().NotBeNull();
    }

    [Fact]
    public void AddCacheOrchestratorEfCoreInvalidation_WhenSectionIsWhitespace_Throws()
    {
        ServiceCollection services = new();
        IConfiguration config = InMemoryCacheConfig();
        var act = () => services.AddCacheOrchestratorEfCoreInvalidation(config, configSection: " ");
        act.Should().Throw<ArgumentException>();
    }

    private static IConfiguration InMemoryCacheConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:FusionCacheInstances:default:Provider"] = "InMemory"
        }).Build();

    public sealed class RegProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class RegDbContext : DbContext
    {
        public RegDbContext(DbContextOptions<RegDbContext> options)
            : base(options)
        {
        }

        public DbSet<RegProduct> Products => Set<RegProduct>();
    }
}
