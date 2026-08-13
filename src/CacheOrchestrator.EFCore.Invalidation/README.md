# CacheOrchestrator.EFCore.Invalidation

Optional **EF Core** SaveChanges hook for [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/).

| | |
|--|--|
| **Provides** | `SaveChanges` interceptor that calls `ICacheOrchestratorInvalidator` |
| **Does not** | Cache EF entities, implement a second-level cache, or own cluster transport |
| **Requires** | `CacheOrchestrator` 2.0+ (`entityKind` on entity identity) |

A domain is a cache **policy** group. This package maps a CLR type to `(domain, entityKind)` and the primary key to `resourceId`. Mapping lives in **code**, next to the model — not in appsettings.

## Install

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

## Register

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.EFCore;

builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(cs);
    opt.AddCacheOrchestratorInvalidation(sp);
});
```

`AddCacheOrchestratorEfCoreInvalidation` does **not** attach to every `DbContext`. Use `AddCacheOrchestratorInvalidation` on the options builder.

Builder callback (same registration):

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddEfCoreInvalidation());
```

## Map types

Pick one (or mix). First match wins:

1. Fluent `CacheInvalidate` on the EF model  
2. `[CacheEntity]` on the CLR type  
3. `Map<T>` at registration  

Unmapped types, owned types, and keyless types are ignored.

### 1. Fluent API in `OnModelCreating` (or `IEntityTypeConfiguration<T>`)

Use this when you do not want to decorate the class (clean domain, types in another assembly).

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>().CacheInvalidate("store", "products");
    modelBuilder.Entity<Asset>().CacheInvalidate("store", "assets");
}
```

```csharp
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        builder.CacheInvalidate("store", "products");
    }
}
```

### 2. Attribute on the entity

```csharp
[CacheEntity("store", "products")]
public class Product
{
    public int Id { get; set; }
}

[CacheEntity("store", "assets")]
public class Asset
{
    public int Id { get; set; }
}
```

### 3. `Map<T>` at composition root

Central catalog next to DI registration. Used when there is no Fluent mapping and no attribute.

```csharp
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration, o =>
{
    o.Map<Product>("store", "products");
    o.Map<Asset>("store", "assets");
});
```

Or on the CacheOrchestrator builder:

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o =>
{
    o.AddEfCoreInvalidation(x =>
    {
        x.Map<Product>("store", "products");
        x.Map<Asset>("store", "assets");
    });
});
```

## Usage

Same domain (`store`) and kind (`products`) on the HTTP cache path and on the EF mapping. The interceptor only runs after a **successful** `SaveChanges` / `SaveChangesAsync` for tracked `Added` / `Modified` / `Deleted` entries.

### Read path (cache the row)

```csharp
app.MapGet("/api/products/{id}", async (
    HttpContext http,
    int id,
    IDomainFusionCache cache,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    Product? product = await cache.GetOrSetEntityAsync(
        http,
        "store",
        "products",
        id.ToString(),
        async ct => await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct),
        cancellationToken);

    return product is null ? Results.NotFound() : Results.Ok(product);
})
.CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");
```

### ChangeTracker — interceptor invalidates after save

Tracked `Add` / property change / `Remove` + `SaveChangesAsync`. No manual `Invalidate*` call.

```csharp
app.MapPut("/api/products/{id}", async (
    int id,
    ProductUpdate body,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    Product? product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    if (product is null)
        return Results.NotFound();

    product.Name = body.Name;
    product.Price = body.Price;

    // SaveChangesAsync uses ChangeTracker, so the interceptor invalidates automatically.
    await db.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});
```

`GET /api/products/{id}` after this is a MISS (OC + Fusion entity tags). Other products in `store` stay cached. List/index endpoints tagged only `domain:store` are **not** evicted.

### Manual invalidation — `Execute*` (no tracker)

`ExecuteUpdate` / `ExecuteDelete` / `ExecuteInsert` never produce `ChangeTracker` entries. Call the invalidator yourself.

```csharp
app.MapPost("/api/products/clearance", async (
    ClearanceRequest body,
    AppDbContext db,
    ICacheOrchestratorInvalidator invalidator,
    CancellationToken cancellationToken) =>
{
    List<int> ids = await db.Products
        .Where(p => p.CategoryId == body.CategoryId)
        .Select(p => p.Id)
        .ToListAsync(cancellationToken);

    await db.Products
        .Where(p => ids.Contains(p.Id))
        .ExecuteUpdateAsync(
            s => s.SetProperty(p => p.Price, p => p.Price * 0.8m),
            cancellationToken);

    // ExecuteUpdateAsync does not use ChangeTracker, so the interceptor does not run.
    // Invalidate explicitly.
    await invalidator.InvalidateEntitiesAsync(
        "store",
        "products",
        ids.Select(id => id.ToString()),
        cancellationToken);

    return Results.Ok(new { updated = ids.Count });
});
```

## Settings (appsettings)

Only operational flags. No type list.

```json
{
  "Cache": {
    "EFCore": {
      "Invalidation": {
        "Enabled": true,
        "BulkThreshold": 20,
        "OnBulk": "Kind"
      }
    }
  }
}
```

| Key | Meaning |
|-----|---------|
| `Enabled` | Master switch (default `true`) |
| `BulkThreshold` | Id count that triggers `OnBulk` (default `20`) |
| `OnBulk` | `Entities` / `Kind` (recommended) / `Domain` (wipes every kind in that policy group) |

## Docs

[ef-core-invalidation.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/ef-core-invalidation.md) · [invalidation.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/invalidation.md) · [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)
