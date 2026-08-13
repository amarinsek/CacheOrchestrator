# CacheOrchestrator.EFCore.Invalidation

EF Core hook for [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/).

Add this package when cached HTTP responses and Fusion entries should follow your database writes. You map an entity type to a cache domain; after a successful `SaveChanges`, the matching rows are dropped from the cache.

## Install

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

## Register

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(cs);
    opt.AddCacheOrchestratorInvalidation(sp);
});
```

Attach the interceptor on each `DbContext` with `AddCacheOrchestratorInvalidation`.

## Map and use

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>().CacheInvalidate("store", "products");
}
```

Use the same domain and kind on the HTTP path:

```csharp
app.MapGet("/api/products/{id}", async (HttpContext http, int id, IDomainFusionCache cache, AppDbContext db, CancellationToken cancellationToken) =>
{
    var product = await cache.GetOrSetEntityAsync(
        http, "store", "products", id.ToString(),
        ct => db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct),
        cancellationToken);

    return product is null ? Results.NotFound() : Results.Ok(product);
})
.CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");
```

A tracked `SaveChanges` then purges that product. `ExecuteUpdate` / `ExecuteDelete` skip the change tracker; call `InvalidateEntitiesAsync` yourself in those handlers.

Attribute and `Map<T>` registration, bulk options, and further examples: [ef-core-invalidation.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/ef-core-invalidation.md).

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
