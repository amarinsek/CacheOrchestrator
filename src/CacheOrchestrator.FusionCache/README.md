# CacheOrchestrator.FusionCache

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) configures Output Cache, application data cache, and client `Cache-Control` under one domain model. It does not replace those systems or own a store.

This package registers ZiggyCreatures **FusionCache** as the **`IDataCacheProvider`** (data cache / DC). It wires named engines from `DataCacheInstances` and owns nested JSON **`FusionCache`** settings (hard TTL, fail-safe, factory timeouts, …). Portable TTL stays under **`DataCache`**.

## Install

```bash
dotnet add package CacheOrchestrator.FusionCache
```

## Example

In an ASP.NET Core host, pair with AspNetCore for Output Cache and HTTP helpers:

```bash
dotnet add package CacheOrchestrator.AspNetCore
dotnet add package CacheOrchestrator.FusionCache
```

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

For a single NuGet reference that already includes AspNetCore + Fusion, see **CacheOrchestrator**.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Microsoft HybridCache data-cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Redis Output Cache store / Fusion L2 / backplane |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) | Multi-instance invalidate / Version / settings bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) | Invalidate after EF `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [FusionCache provider](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/fusion-cache.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
