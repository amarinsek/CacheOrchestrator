# CacheOrchestrator.EFCore.Invalidation

EF Core **SaveChanges** hook for CacheOrchestrator. Map a CLR type to `(domain, entityKind)`; after a successful save, matching entity tags are purged via `ICacheOrchestratorInvalidator`.

## Install

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

## Quick start

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(connectionString);
    opt.AddCacheOrchestratorInvalidation(sp);
});
```

```csharp
modelBuilder.Entity<Product>().CacheInvalidate("catalog", "products");
// or [CacheEntity("catalog", "products")] / Map<T> at DI
```

Use the **same** domain and `entityKind` on the HTTP / library cache path. Tracked `SaveChanges` invalidates automatically; `ExecuteUpdate` / `ExecuteDelete` require manual `Invalidate*`.

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md) (§8–§9)
- [EF Core invalidation](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/ef-core-invalidation.md)
- [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
