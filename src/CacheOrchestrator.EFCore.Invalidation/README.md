# CacheOrchestrator.EFCore.Invalidation

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package hooks EF Core **`SaveChanges`**: map a CLR type to `(domain, entityKind)`; after a successful save, matching entity tags are purged through `ICacheOrchestratorInvalidator`.

## Install

```bash
dotnet add package CacheOrchestrator.EFCore.Invalidation --prerelease
```

## Configuration

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

## Usage

```bash
dotnet add package CacheOrchestrator --prerelease
dotnet add package CacheOrchestrator.EFCore.Invalidation --prerelease
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
modelBuilder.Entity<Product>().CacheInvalidate("catalog", "products");
```

Tracked `SaveChanges` invalidates matching entity tags after a successful save. `ExecuteUpdate` and `ExecuteDelete` bypass the change tracker and require explicit invalidation.

## Documentation

- [EF Core invalidation](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/ef-core-invalidation.md)
- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
