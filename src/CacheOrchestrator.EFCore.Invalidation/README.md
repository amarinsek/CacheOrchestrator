# CacheOrchestrator.EFCore.Invalidation

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) configures Output Cache, application **data cache**, and client `Cache-Control` under one **domain** model. It does not replace those systems or own a store.

This package hooks EF Core **`SaveChanges`**: map a CLR type to `(domain, entityKind)`; after a successful save, matching entity tags are purged through `ICacheOrchestratorInvalidator`.

## Install

```bash
dotnet add package CacheOrchestrator.EFCore.Invalidation
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
```

```csharp
modelBuilder.Entity<Product>().CacheInvalidate("catalog", "products");
```

Use the same domain and `entityKind` on the HTTP / library cache path. Tracked `SaveChanges` invalidates automatically; `ExecuteUpdate` / `ExecuteDelete` require manual `Invalidate*`.

Full GET + PUT samples: [packages.md §8–§9](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md).

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
