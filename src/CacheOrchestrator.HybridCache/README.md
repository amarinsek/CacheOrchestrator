# CacheOrchestrator.HybridCache

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) configures Output Cache, application **data cache**, and client `Cache-Control` under one **domain** model. It does not replace those systems or own a store.

This package registers Microsoft **HybridCache** as the **`IDataCacheProvider`**. It uses portable **`DataCache.Ttl`** only. Fusion-specific options (fail-safe, hard TTL, factory timeouts, named data-cache instances) are not applied.

## Install

```bash
dotnet add package CacheOrchestrator.HybridCache
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

## Example

In an ASP.NET Core host:

```bash
dotnet add package CacheOrchestrator.AspNetCore
dotnet add package CacheOrchestrator.HybridCache
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

```csharp
builder.Services.AddHybridCache();
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorHybridCache();

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Optional L2: configure HybridCache / `IDistributedCache` as usual (outside this package). Prefer **CacheOrchestrator.FusionCache** when you need fail-safe and the full Fusion surface.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/) | FusionCache data-cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Redis Output Cache store / Fusion L2 / backplane |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) | Multi-instance invalidate / Version / settings bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) | Invalidate after EF `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
