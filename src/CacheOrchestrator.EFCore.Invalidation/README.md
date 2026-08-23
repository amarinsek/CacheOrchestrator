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
        http,
        ct => db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct),
        cancellationToken);

    return product is null ? Results.NotFound() : Results.Ok(product);
})
.CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");
```

The interceptor relies on the **EF Core Change Tracker**. When you call `SaveChanges` or `SaveChangesAsync`, it automatically finds any mapped entities in the `Added`, `Modified`, or `Deleted` state and invalidates them from the cache upon a successful save.

> [!WARNING]
> **Bulk operations skip the Change Tracker!**
> If you use `ExecuteUpdateAsync()` or `ExecuteDeleteAsync()`, the interceptor will **not** detect those changes because the entities are never loaded into memory. In these cases, you must manually trigger the cache invalidation by calling `ICacheOrchestratorInvalidator.InvalidateEntitiesAsync()` yourself.

Attribute and `Map<T>` registration, bulk options, and further examples: [ef-core-invalidation.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/ef-core-invalidation.md). Orientation: [Guide — topologies](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/topologies.md).

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
