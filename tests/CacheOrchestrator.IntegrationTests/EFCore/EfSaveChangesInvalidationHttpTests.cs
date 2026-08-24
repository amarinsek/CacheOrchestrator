using System.Net;
using System.Net.Http.Json;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.EFCore;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.IntegrationTests.EFCore;

/// <summary>
/// One-host TestServer: real Fusion + Output Cache + EF InMemory.
/// Tracked SaveChanges must evict that entity so the next GET is a MISS.
/// </summary>
public class EfSaveChangesInvalidationHttpTests
{
    private sealed class FactoryCounter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    [Fact]
    public async Task SaveChanges_OnTrackedProduct_NextGetIsMiss_SiblingStaysHit()
    {
        string domain = "store-" + Guid.NewGuid().ToString("N")[..8];
        string dbName = "ef-it-" + Guid.NewGuid().ToString("N");

        Dictionary<string, string?> configValues = new()
        {
            ["Cache:OutputCache:Provider"] = "InMemory",
            ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
            ["Cache:EmitDiagnosticsHeaders"] = "true",
            [$"Cache:Domains:{domain}:Version"] = "v1",
            [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
            [$"Cache:Domains:{domain}:ClientCache:Ttl"] = "00:01:00",
            [$"Cache:Domains:{domain}:ClientCache:TtlMin"] = "00:01:00",
            [$"Cache:Domains:{domain}:OutputCache:Ttl"] = "00:02:00",
            [$"Cache:Domains:{domain}:DataCache:Ttl"] = "00:05:00",
            [$"Cache:Domains:{domain}:FusionCache:Jitter"] = "00:00:00",
            [$"Cache:Domains:{domain}:FusionCache:EagerRefreshRatio"] = "0",
        };

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddCacheOrchestrator(config);
        builder.Services.AddCacheOrchestratorEfCoreInvalidation(
            config,
            o => o.Map<Product>(domain, "products"));
        builder.Services.AddSingleton<FactoryCounter>();
        builder.Services.AddDbContext<CatalogDbContext>((sp, opt) =>
        {
            opt.UseInMemoryDatabase(dbName);
            opt.AddCacheOrchestratorInvalidation(sp);
        });

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        app.MapGet("/api/products/{id:int}", async (
            HttpContext http,
            int id,
            IDomainFusionCache cache,
            CatalogDbContext db,
            FactoryCounter factories,
            CancellationToken cancellationToken) =>
        {
            Product? product = await cache.GetOrSetEntityAsync(
                http,
                async ct =>
                {
                    factories.Increment();
                    return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
                },
                cancellationToken);

            return product is null ? Results.NotFound() : Results.Json(product);
        }).CacheOutputWithDomain(domain, resourceRouteKey: "id", entityKind: "products");

        app.MapPut("/api/products/{id:int}", async (
            int id,
            ProductUpdate body,
            CatalogDbContext db,
            CancellationToken cancellationToken) =>
        {
            Product? product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (product is null)
                return Results.NotFound();

            product.Name = body.Name;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        await using (app)
        {
            await SeedAsync(app);
            await app.StartAsync(TestContext.Current.CancellationToken);
            HttpClient client = app.GetTestClient();
            FactoryCounter factories = app.Services.GetRequiredService<FactoryCounter>();

            (_, string miss1, string body1) = await GetAsync(client, "/api/products/1");
            miss1.Should().Contain("oc=miss");
            body1.Should().Contain("Widget");
            factories.Count.Should().Be(1);

            (_, string miss2, _) = await GetAsync(client, "/api/products/2");
            miss2.Should().Contain("oc=miss");
            factories.Count.Should().Be(2);

            (_, string hit1, _) = await GetAsync(client, "/api/products/1");
            hit1.Should().Contain("oc=hit");
            (_, string hit2, _) = await GetAsync(client, "/api/products/2");
            hit2.Should().Contain("oc=hit");
            factories.Count.Should().Be(2);

            HttpResponseMessage put = await client.PutAsJsonAsync(
                "/api/products/1",
                new ProductUpdate("Gadget"),
                TestContext.Current.CancellationToken);
            put.StatusCode.Should().Be(HttpStatusCode.NoContent);

            (_, string after1, string bodyAfter1) = await GetAsync(client, "/api/products/1");
            after1.Should().Contain("oc=miss");
            bodyAfter1.Should().Contain("Gadget");
            factories.Count.Should().Be(3, "SaveChanges must evict Fusion so the factory runs again");

            (_, string after2, string bodyAfter2) = await GetAsync(client, "/api/products/2");
            after2.Should().Contain("oc=hit", "sibling product must stay cached");
            bodyAfter2.Should().Contain("Other");
            factories.Count.Should().Be(3);
        }
    }

    [Fact]
    public async Task GetOrSetEntityAsync_WithoutDomain_UsesOutputCacheDomain()
    {
        string domain = "store-" + Guid.NewGuid().ToString("N")[..8];
        string dbName = "ef-it-nodom-" + Guid.NewGuid().ToString("N");

        await using WebApplication app = await StartCatalogAppAsync(
            domain,
            dbName,
            mapGet: (a, d) =>
            {
                a.MapGet("/api/products/{id:int}", async (
                    HttpContext http,
                    int id,
                    IDomainFusionCache cache,
                    CatalogDbContext db,
                    FactoryCounter factories,
                    CancellationToken cancellationToken) =>
                {
                    Product? product = await cache.GetOrSetEntityAsync(
                        http,
                        async ct =>
                        {
                            factories.Increment();
                            return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
                        },
                        cancellationToken);
                    return product is null ? Results.NotFound() : Results.Json(product);
                }).CacheOutputWithDomain(d, resourceRouteKey: "id", entityKind: "products");
            });

        await SeedAsync(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        HttpClient client = app.GetTestClient();
        FactoryCounter factories = app.Services.GetRequiredService<FactoryCounter>();

        (_, string miss, string body) = await GetAsync(client, "/api/products/1");
        miss.Should().Contain("oc=miss");
        body.Should().Contain("Widget");
        factories.Count.Should().Be(1);

        (_, string hit, _) = await GetAsync(client, "/api/products/1");
        hit.Should().Contain("oc=hit");
        factories.Count.Should().Be(1);
    }

    [Fact]
    public async Task GuidRoute_SaveChanges_InvalidatesMatchingResource()
    {
        string domain = "store-" + Guid.NewGuid().ToString("N")[..8];
        string dbName = "ef-it-guid-" + Guid.NewGuid().ToString("N");
        Guid id = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

        Dictionary<string, string?> configValues = BaseConfig(domain);
        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        WebApplicationBuilder builder = CreateBuilder();
        builder.Services.AddCacheOrchestrator(config);
        builder.Services.AddCacheOrchestratorEfCoreInvalidation(
            config,
            o => o.Map<GuidProduct>(domain, "products"));
        builder.Services.AddSingleton<FactoryCounter>();
        builder.Services.AddDbContext<GuidCatalogDbContext>((sp, opt) =>
        {
            opt.UseInMemoryDatabase(dbName);
            opt.AddCacheOrchestratorInvalidation(sp);
        });

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();

        app.MapGet("/api/g/{id:guid}", async (
            HttpContext http,
            Guid id,
            IDomainFusionCache cache,
            GuidCatalogDbContext db,
            FactoryCounter factories,
            CancellationToken cancellationToken) =>
        {
            GuidProduct? product = await cache.GetOrSetEntityAsync(
                http,
                async ct =>
                {
                    factories.Increment();
                    return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
                },
                cancellationToken);
            return product is null ? Results.NotFound() : Results.Json(product);
        }).CacheOutputWithDomain(domain, resourceRouteKey: "id", entityKind: "products");

        app.MapPut("/api/g/{id:guid}", async (
            Guid id,
            ProductUpdate body,
            GuidCatalogDbContext db,
            CancellationToken cancellationToken) =>
        {
            GuidProduct? product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (product is null)
                return Results.NotFound();
            product.Name = body.Name;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        await using (app)
        {
            await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
            {
                GuidCatalogDbContext db = scope.ServiceProvider.GetRequiredService<GuidCatalogDbContext>();
                db.Products.Add(new GuidProduct { Id = id, Name = "Widget" });
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await app.StartAsync(TestContext.Current.CancellationToken);
            HttpClient client = app.GetTestClient();
            FactoryCounter factories = app.Services.GetRequiredService<FactoryCounter>();

            string path = "/api/g/" + id.ToString("D").ToUpperInvariant();
            (_, string miss, _) = await GetAsync(client, path);
            miss.Should().Contain("oc=miss");
            factories.Count.Should().Be(1);

            (_, string hit, _) = await GetAsync(client, path);
            hit.Should().Contain("oc=hit");

            (await client.PutAsJsonAsync(path, new ProductUpdate("Gadget"), TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            (_, string after, string body) = await GetAsync(client, path);
            after.Should().Contain("oc=miss");
            body.Should().Contain("Gadget");
            factories.Count.Should().Be(2);
        }
    }

    [Fact]
    public async Task ExecuteUpdate_DoesNotEvict_UntilManualInvalidate()
    {
        string domain = "store-" + Guid.NewGuid().ToString("N")[..8];
        await using Microsoft.Data.Sqlite.SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(BaseConfig(domain)).Build();
        WebApplicationBuilder builder = CreateBuilder();
        builder.Services.AddCacheOrchestrator(config);
        builder.Services.AddCacheOrchestratorEfCoreInvalidation(
            config,
            o => o.Map<Product>(domain, "products"));
        builder.Services.AddSingleton<FactoryCounter>();
        builder.Services.AddDbContext<CatalogDbContext>((sp, opt) =>
        {
            opt.UseSqlite(connection);
            opt.AddCacheOrchestratorInvalidation(sp);
        });

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/api/products/{id:int}", async (
            HttpContext http,
            int id,
            IDomainFusionCache cache,
            CatalogDbContext db,
            FactoryCounter factories,
            CancellationToken cancellationToken) =>
        {
            Product? product = await cache.GetOrSetEntityAsync(
                http,
                async ct =>
                {
                    factories.Increment();
                    return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
                },
                cancellationToken);
            return product is null ? Results.NotFound() : Results.Json(product);
        }).CacheOutputWithDomain(domain, resourceRouteKey: "id", entityKind: "products");

        await using (app)
        {
            await using (AsyncServiceScope create = app.Services.CreateAsyncScope())
            {
                CatalogDbContext db = create.ServiceProvider.GetRequiredService<CatalogDbContext>();
                await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            }

            await SeedAsync(app);
            await app.StartAsync(TestContext.Current.CancellationToken);
            HttpClient client = app.GetTestClient();
            FactoryCounter factories = app.Services.GetRequiredService<FactoryCounter>();

            await GetAsync(client, "/api/products/1");
            (_, string hit, _) = await GetAsync(client, "/api/products/1");
            hit.Should().Contain("oc=hit");
            factories.Count.Should().Be(1);

            await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
            {
                CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                await db.Products
                    .Where(p => p.Id == 1)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(p => p.Name, "Bulk"),
                        TestContext.Current.CancellationToken);
            }

            (_, string stillHit, string staleBody) = await GetAsync(client, "/api/products/1");
            stillHit.Should().Contain("oc=hit", "ExecuteUpdate must not run the interceptor");
            staleBody.Should().Contain("Widget");
            factories.Count.Should().Be(1);

            await app.Services.GetRequiredService<ICacheOrchestratorInvalidator>()
                .InvalidateEntityAsync(domain, "products", "1", TestContext.Current.CancellationToken);

            (_, string miss, string fresh) = await GetAsync(client, "/api/products/1");
            miss.Should().Contain("oc=miss");
            fresh.Should().Contain("Bulk");
            factories.Count.Should().Be(2);
        }
    }

    [Fact]
    public async Task CacheEntityAttribute_WithoutMap_SaveChanges_Invalidates()
    {
        const string domain = "attr-http-store";
        string dbName = "ef-it-attr-" + Guid.NewGuid().ToString("N");

        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(BaseConfig(domain)).Build();
        WebApplicationBuilder builder = CreateBuilder();
        builder.Services.AddCacheOrchestrator(config);
        builder.Services.AddCacheOrchestratorEfCoreInvalidation(config);
        builder.Services.AddSingleton<FactoryCounter>();
        builder.Services.AddDbContext<AttrCatalogDbContext>((sp, opt) =>
        {
            opt.UseInMemoryDatabase(dbName);
            opt.AddCacheOrchestratorInvalidation(sp);
        });

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        app.MapGet("/api/attr/{id:int}", async (
            HttpContext http,
            int id,
            IDomainFusionCache cache,
            AttrCatalogDbContext db,
            FactoryCounter factories,
            CancellationToken cancellationToken) =>
        {
            AttrProduct? product = await cache.GetOrSetEntityAsync(
                http,
                async ct =>
                {
                    factories.Increment();
                    return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
                },
                cancellationToken);
            return product is null ? Results.NotFound() : Results.Json(product);
        }).CacheOutputWithDomain(domain, resourceRouteKey: "id", entityKind: "products");

        app.MapPut("/api/attr/{id:int}", async (
            int id,
            ProductUpdate body,
            AttrCatalogDbContext db,
            CancellationToken cancellationToken) =>
        {
            AttrProduct? product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (product is null)
                return Results.NotFound();
            product.Name = body.Name;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        await using (app)
        {
            await using (AsyncServiceScope seed = app.Services.CreateAsyncScope())
            {
                AttrCatalogDbContext db = seed.ServiceProvider.GetRequiredService<AttrCatalogDbContext>();
                db.Products.Add(new AttrProduct { Id = 1, Name = "Widget" });
                db.Products.Add(new AttrProduct { Id = 2, Name = "Other" });
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await app.StartAsync(TestContext.Current.CancellationToken);
            HttpClient client = app.GetTestClient();
            FactoryCounter factories = app.Services.GetRequiredService<FactoryCounter>();

            (_, string miss, _) = await GetAsync(client, "/api/attr/1");
            miss.Should().Contain("oc=miss");
            factories.Count.Should().Be(1);

            (_, string hit, _) = await GetAsync(client, "/api/attr/1");
            hit.Should().Contain("oc=hit");

            (_, string sibling, _) = await GetAsync(client, "/api/attr/2");
            sibling.Should().Contain("oc=miss");
            factories.Count.Should().Be(2);

            (await client.PutAsJsonAsync("/api/attr/1", new ProductUpdate("Gadget"), TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            (_, string after1, string body1) = await GetAsync(client, "/api/attr/1");
            after1.Should().Contain("oc=miss");
            body1.Should().Contain("Gadget");
            factories.Count.Should().Be(3);

            (_, string after2, string body2) = await GetAsync(client, "/api/attr/2");
            after2.Should().Contain("oc=hit");
            body2.Should().Contain("Other");
        }
    }

    private static Dictionary<string, string?> BaseConfig(string domain) => new()
    {
        ["Cache:OutputCache:Provider"] = "InMemory",
        ["Cache:DataCacheInstances:default:Provider"] = "InMemory",
        ["Cache:EmitDiagnosticsHeaders"] = "true",
        [$"Cache:Domains:{domain}:Version"] = "v1",
        [$"Cache:Domains:{domain}:ClientCache:Cacheability"] = "Public",
        [$"Cache:Domains:{domain}:ClientCache:Ttl"] = "00:01:00",
        [$"Cache:Domains:{domain}:ClientCache:TtlMin"] = "00:01:00",
        [$"Cache:Domains:{domain}:OutputCache:Ttl"] = "00:02:00",
        [$"Cache:Domains:{domain}:DataCache:Ttl"] = "00:05:00",
        [$"Cache:Domains:{domain}:FusionCache:Jitter"] = "00:00:00",
        [$"Cache:Domains:{domain}:FusionCache:EagerRefreshRatio"] = "0",
    };

    private static WebApplicationBuilder CreateBuilder()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        return builder;
    }

    private static async Task<WebApplication> StartCatalogAppAsync(
        string domain,
        string dbName,
        Action<WebApplication, string> mapGet)
    {
        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(BaseConfig(domain)).Build();
        WebApplicationBuilder builder = CreateBuilder();
        builder.Services.AddCacheOrchestrator(config);
        builder.Services.AddCacheOrchestratorEfCoreInvalidation(
            config,
            o => o.Map<Product>(domain, "products"));
        builder.Services.AddSingleton<FactoryCounter>();
        builder.Services.AddDbContext<CatalogDbContext>((sp, opt) =>
        {
            opt.UseInMemoryDatabase(dbName);
            opt.AddCacheOrchestratorInvalidation(sp);
        });

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCacheOrchestrator();
        mapGet(app, domain);
        return app;
    }

    private static async Task SeedAsync(WebApplication app)
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        CatalogDbContext db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Products.Add(new Product { Id = 1, Name = "Widget" });
        db.Products.Add(new Product { Id = 2, Name = "Other" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<(HttpResponseMessage Res, string XCache, string Body)> GetAsync(
        HttpClient client,
        string url)
    {
        HttpResponseMessage res = await client.GetAsync(url, TestContext.Current.CancellationToken);
        string body = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string xCache = res.Headers.TryGetValues("X-Cache", out IEnumerable<string>? values)
            ? string.Join(",", values)
            : string.Empty;
        return (res, xCache, body);
    }

    public sealed class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed record ProductUpdate(string Name);

    public sealed class CatalogDbContext : DbContext
    {
        public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
            : base(options)
        {
        }

        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>().Property(p => p.Id).ValueGeneratedNever();
        }
    }

    public sealed class GuidProduct
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class GuidCatalogDbContext : DbContext
    {
        public GuidCatalogDbContext(DbContextOptions<GuidCatalogDbContext> options)
            : base(options)
        {
        }

        public DbSet<GuidProduct> Products => Set<GuidProduct>();
    }

    [CacheEntity("attr-http-store", "products")]
    public sealed class AttrProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class AttrCatalogDbContext : DbContext
    {
        public AttrCatalogDbContext(DbContextOptions<AttrCatalogDbContext> options)
            : base(options)
        {
        }

        public DbSet<AttrProduct> Products => Set<AttrProduct>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AttrProduct>().Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
