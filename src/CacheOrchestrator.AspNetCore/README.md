# CacheOrchestrator.AspNetCore

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) configures Output Cache, application **data cache**, and client `Cache-Control` under one **domain** model. It does not replace those systems or own a store.

This package is the **ASP.NET Core host** layer: Output Cache domain policies, client Cache-Control, Admin API, vary rules, and HTTP **`IDomainDataCache`** (a thin projection over Core `ICacheOrchestrator`). It depends on **Core** only. You still need a data-cache provider package (Fusion or Hybrid) unless you use Output Cache alone.

## Install

```bash
dotnet add package CacheOrchestrator.AspNetCore
```

## Example

With Fusion as the data engine (install that package as well):

```bash
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

Without `.CacheOutputWithDomain` / `[CacheDomain]`, Output Cache does not store (base policy is `NoCache`).

For a single NuGet reference that already includes AspNetCore + Fusion, see **CacheOrchestrator**.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/) | FusionCache data-cache provider |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Microsoft HybridCache data-cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Redis Output Cache store / Fusion L2 / backplane |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) | Multi-instance invalidate / Version / settings bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) | Invalidate after EF `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [Output Cache](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/output-cache.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
