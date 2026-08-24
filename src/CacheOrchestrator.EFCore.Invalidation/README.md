# CacheOrchestrator.EFCore.Invalidation

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) unifies the configuration of Output Cache, data cache, and client Cache-Control within a single domain model. It ensures seamless coordination and cache invalidation across all layers while significantly reducing boilerplate code.

This package hooks EF Core **`SaveChanges`**: map a CLR type to `(domain, entityKind)`; after a successful save, matching entity tags are purged through `ICacheOrchestratorInvalidator`.

## Install

```bash
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

## Config

Domain policy (same as the rest of CacheOrchestrator). Optional interceptor options:

```json
{
  "Cache": {
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "TtlSeconds": 300 },
        "OutputCache": { "TtlSeconds": 60 }
      }
    },
    "EFCore": {
      "Invalidation": {
        "Enabled": true
      }
    }
  }
}
```

## Example

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(connectionString);
    opt.AddCacheOrchestratorInvalidation(sp);
});

// In OnModelCreating (or Map<T> / [CacheEntity]):
// modelBuilder.Entity<Product>().CacheInvalidate("catalog", "products");

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache, AppDbContext db) =>
{
    var data = await cache.GetOrSetEntityAsync(http, async ct =>
    {
        Product? p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id.ToString() == id, ct);
        return p is null ? null : new ProductDto(p.Id, p.Price);
    });
    return data is null ? Results.NotFound() : Results.Json(data);
})
.CacheOutputWithDomain("catalog", resourceRouteKey: "id", entityKind: "products");

app.MapPut("/api/products/{id}", async (string id, UpdatePriceBody body, AppDbContext db, CancellationToken ct) =>
{
    Product product = await db.Products.SingleAsync(x => x.Id.ToString() == id, ct);
    product.Price = body.Price;
    await db.SaveChangesAsync(ct); // interceptor invalidates — no manual Invalidate*
    return Results.NoContent();
});
```

Tracked `SaveChanges` invalidates automatically; `ExecuteUpdate` / `ExecuteDelete` require manual `Invalidate*`. More layouts: [packages.md §8–§9](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md).

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/) | FusionCache data-cache provider |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Microsoft HybridCache data-cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Redis Output Cache store / Fusion L2 / backplane |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) | Multi-instance invalidate / Version / settings bus |

## Documentation

- [EF Core invalidation](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/ef-core-invalidation.md)
- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
